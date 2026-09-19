#pragma once
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

// Replaces consoleInit/printf/consoleUpdate with a manually-drawn
// framebuffer text console that can render Japanese (and any other
// non-ASCII text) — consoleInit's built-in bitmap font only has Latin/
// extended-ASCII glyphs, so it renders Japanese as garbage. This one
// rasterizes glyphs from the console's own shared system font (the same
// font the OS itself uses for Japanese UI text) via stb_truetype, with a
// bitmap cache so repeat characters (menu chrome, kana/kanji reused across
// frames) don't get re-rasterized every frame.
//
// API intentionally mirrors consoleInit/printf/consoleUpdate/consoleExit
// so callers barely change: textRenderClear() resets the cursor to the
// top-left margin, textRenderPrintf() behaves like printf() including
// '\n' handling (advances to the next line), textRenderPresent() blits
// the frame and waits for vsync.
bool textRenderInit(void);
void textRenderExit(void);
void textRenderClear(void);
void textRenderPrintf(const char *fmt, ...) __attribute__((format(printf, 1, 2)));
void textRenderPresent(void);
