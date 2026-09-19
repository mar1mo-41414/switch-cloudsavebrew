#include "text_render.h"
#include <switch.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define STB_TRUETYPE_IMPLEMENTATION
#include "vendor/stb_truetype.h"

// Fixed virtual resolution — same approach consoleInit itself uses
// internally (draw at one fixed size, let the compositor scale for
// handheld vs docked) so we don't need to handle resize.
#define FB_WIDTH  1280
#define FB_HEIGHT 720

#define TEXT_PIXEL_HEIGHT 22.0f
#define LINE_SPACING_EXTRA 6
#define MARGIN_X 32
#define MARGIN_Y 32

typedef struct {
    uint32_t codepoint;
    int width, height;
    int xoff, yoff;   // offset from pen origin to bitmap top-left
    int advance;      // pixel advance, already scaled
    uint8_t *bitmap;  // width*height 8-bit coverage, NULL if empty glyph (e.g. space)
} Glyph;

#define GLYPH_CACHE_MAX 1024
static Glyph s_glyphCache[GLYPH_CACHE_MAX];
static int s_glyphCount = 0;

static Framebuffer s_fb;
static stbtt_fontinfo s_font;
static PlFontData s_fontData;
static float s_scale;
static int s_ascentPx;
static int s_lineHeight;

static char *s_textBuf = NULL;
static size_t s_textBufCap = 0;
static size_t s_textBufLen = 0;

static int utf8Decode(const char *s, uint32_t *out)
{
    uint8_t c0 = (uint8_t)s[0];
    if (c0 < 0x80) { *out = c0; return 1; }
    if ((c0 & 0xE0) == 0xC0 && s[1]) { *out = ((uint32_t)(c0 & 0x1F) << 6) | (uint8_t)(s[1] & 0x3F); return 2; }
    if ((c0 & 0xF0) == 0xE0 && s[1] && s[2]) {
        *out = ((uint32_t)(c0 & 0x0F) << 12) | ((uint32_t)(s[1] & 0x3F) << 6) | (uint8_t)(s[2] & 0x3F);
        return 3;
    }
    if ((c0 & 0xF8) == 0xF0 && s[1] && s[2] && s[3]) {
        *out = ((uint32_t)(c0 & 0x07) << 18) | ((uint32_t)(s[1] & 0x3F) << 12) |
               ((uint32_t)(s[2] & 0x3F) << 6) | (uint8_t)(s[3] & 0x3F);
        return 4;
    }
    *out = c0;
    return 1; // invalid lead byte — treat as a single byte so we don't get stuck
}

// Cache is a flat array with linear search rather than a hash map: the
// distinct glyph count for this app's UI text + a handful of Japanese
// game titles is at most a few hundred, so a linear scan is cheap enough
// and this stays simple.
static Glyph *getGlyph(uint32_t cp)
{
    for (int i = 0; i < s_glyphCount; i++)
        if (s_glyphCache[i].codepoint == cp)
            return &s_glyphCache[i];

    if (s_glyphCount >= GLYPH_CACHE_MAX)
        return NULL; // extremely unlikely for this app's text; just skip further glyphs

    Glyph *g = &s_glyphCache[s_glyphCount];
    g->codepoint = cp;

    int advance, lsb;
    stbtt_GetCodepointHMetrics(&s_font, (int)cp, &advance, &lsb);
    g->advance = (int)(advance * s_scale + 0.5f);

    int x0, y0, x1, y1;
    stbtt_GetCodepointBitmapBox(&s_font, (int)cp, s_scale, s_scale, &x0, &y0, &x1, &y1);
    g->xoff = x0;
    g->yoff = y0;
    g->width = x1 - x0;
    g->height = y1 - y0;

    if (g->width > 0 && g->height > 0)
    {
        g->bitmap = malloc((size_t)g->width * (size_t)g->height);
        if (g->bitmap)
            stbtt_MakeCodepointBitmap(&s_font, g->bitmap, g->width, g->height, g->width, s_scale, s_scale, (int)cp);
    }
    else
    {
        g->bitmap = NULL;
    }

    s_glyphCount++;
    return g;
}

static void blitGlyph(uint32_t *fb, uint32_t stridePx, int penX, int baselineY, const Glyph *g)
{
    if (!g->bitmap)
        return;

    int startX = penX + g->xoff;
    int startY = baselineY + g->yoff;

    for (int y = 0; y < g->height; y++)
    {
        int py = startY + y;
        if (py < 0 || py >= FB_HEIGHT)
            continue;

        for (int x = 0; x < g->width; x++)
        {
            int px = startX + x;
            if (px < 0 || px >= FB_WIDTH)
                continue;

            uint8_t a = g->bitmap[y * g->width + x];
            if (a == 0)
                continue;

            // White text over whatever's already drawn (we clear to
            // opaque black first, so in practice this is just coverage
            // scaling, but a real blend keeps overlapping glyphs sane).
            uint32_t *dst = &fb[py * stridePx + px];
            uint8_t dr = (uint8_t)(*dst >> 0);
            uint8_t dg = (uint8_t)(*dst >> 8);
            uint8_t db = (uint8_t)(*dst >> 16);
            uint8_t r = (uint8_t)((255 * a + dr * (255 - a)) / 255);
            uint8_t gr = (uint8_t)((255 * a + dg * (255 - a)) / 255);
            uint8_t b = (uint8_t)((255 * a + db * (255 - a)) / 255);
            *dst = 0xFF000000u | ((uint32_t)b << 16) | ((uint32_t)gr << 8) | r;
        }
    }
}

bool textRenderInit(void)
{
    Result rc = plInitialize(PlServiceType_User);
    if (R_FAILED(rc))
        return false;

    // Standard covers Latin + the kana/kanji set used for Japanese system
    // UI text — the same font the console's own menus render Japanese
    // with, so anything the OS can display, we can too.
    rc = plGetSharedFontByType(&s_fontData, PlSharedFontType_Standard);
    if (R_FAILED(rc))
    {
        plExit();
        return false;
    }

    int offset = stbtt_GetFontOffsetForIndex((const unsigned char *)s_fontData.address, 0);
    if (offset < 0 || !stbtt_InitFont(&s_font, (const unsigned char *)s_fontData.address, offset))
    {
        plExit();
        return false;
    }

    s_scale = stbtt_ScaleForPixelHeight(&s_font, TEXT_PIXEL_HEIGHT);
    int ascent, descent, lineGap;
    stbtt_GetFontVMetrics(&s_font, &ascent, &descent, &lineGap);
    s_ascentPx = (int)(ascent * s_scale + 0.5f);
    s_lineHeight = (int)((ascent - descent + lineGap) * s_scale + 0.5f) + LINE_SPACING_EXTRA;

    framebufferCreate(&s_fb, nwindowGetDefault(), FB_WIDTH, FB_HEIGHT, PIXEL_FORMAT_RGBA_8888, 2);
    framebufferMakeLinear(&s_fb);

    return true;
}

void textRenderExit(void)
{
    framebufferClose(&s_fb);

    for (int i = 0; i < s_glyphCount; i++)
        free(s_glyphCache[i].bitmap);
    s_glyphCount = 0;

    free(s_textBuf);
    s_textBuf = NULL;
    s_textBufCap = 0;
    s_textBufLen = 0;

    plExit();
}

void textRenderClear(void)
{
    s_textBufLen = 0;
    if (s_textBufCap > 0)
        s_textBuf[0] = '\0';
}

void textRenderPrintf(const char *fmt, ...)
{
    char tmp[1024];
    va_list ap;
    va_start(ap, fmt);
    int n = vsnprintf(tmp, sizeof(tmp), fmt, ap);
    va_end(ap);
    if (n <= 0)
        return;
    if ((size_t)n >= sizeof(tmp))
        n = sizeof(tmp) - 1; // truncated — still better than corrupting the buffer

    size_t need = s_textBufLen + (size_t)n + 1;
    if (need > s_textBufCap)
    {
        size_t newCap = s_textBufCap ? s_textBufCap * 2 : 4096;
        while (newCap < need)
            newCap *= 2;
        char *nb = realloc(s_textBuf, newCap);
        if (!nb)
            return;
        s_textBuf = nb;
        s_textBufCap = newCap;
    }

    memcpy(s_textBuf + s_textBufLen, tmp, (size_t)n);
    s_textBufLen += (size_t)n;
    s_textBuf[s_textBufLen] = '\0';
}

void textRenderPresent(void)
{
    u32 stride;
    uint32_t *fb = (uint32_t *)framebufferBegin(&s_fb, &stride);
    uint32_t stridePx = stride / sizeof(uint32_t);

    for (int y = 0; y < FB_HEIGHT; y++)
        for (int x = 0; x < FB_WIDTH; x++)
            fb[(size_t)y * stridePx + x] = 0xFF000000u;

    int penX = MARGIN_X, penY = MARGIN_Y;
    const char *p = s_textBuf ? s_textBuf : "";
    while (*p)
    {
        if (*p == '\n')
        {
            penX = MARGIN_X;
            penY += s_lineHeight;
            p++;
            continue;
        }

        uint32_t cp;
        int len = utf8Decode(p, &cp);
        p += len;
        if (cp == '\r')
            continue;

        Glyph *g = getGlyph(cp);
        if (g)
        {
            blitGlyph(fb, stridePx, penX, penY + s_ascentPx, g);
            penX += g->advance;
        }
    }

    framebufferEnd(&s_fb);
}
