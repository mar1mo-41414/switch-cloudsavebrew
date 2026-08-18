#include "config.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

#define CONFIG_PATH "sdmc:/switch/switch-cloudsavebrew/config.ini"

static void trim(char *s)
{
    // strip trailing \r\n and whitespace
    size_t len = strlen(s);
    while (len > 0 && (s[len - 1] == '\n' || s[len - 1] == '\r' || s[len - 1] == ' '))
        s[--len] = '\0';
}

static void copy_field(char *dst, size_t dst_len, const char *src)
{
    snprintf(dst, dst_len, "%s", src);
}

bool configLoad(Config *cfg, char *err_out, size_t err_out_len)
{
    memset(cfg, 0, sizeof(*cfg));

    FILE *f = fopen(CONFIG_PATH, "r");
    if (!f)
    {
        if (err_out)
            snprintf(err_out, err_out_len, "config not found:\n%s", CONFIG_PATH);
        return false;
    }

    char line[512];
    char section[32] = "";

    while (fgets(line, sizeof(line), f))
    {
        trim(line);

        if (line[0] == '\0' || line[0] == ';' || line[0] == '#')
            continue;

        if (line[0] == '[')
        {
            char *end = strchr(line, ']');
            if (end)
            {
                *end = '\0';
                copy_field(section, sizeof(section), line + 1);
            }
            continue;
        }

        char *eq = strchr(line, '=');
        if (!eq)
            continue;
        *eq = '\0';
        const char *key = line;
        const char *value = eq + 1;

        if (strcmp(section, "remote") == 0)
        {
            if (strcmp(key, "api_base") == 0)
                copy_field(cfg->api_base, sizeof(cfg->api_base), value);
            else if (strcmp(key, "owner") == 0)
                copy_field(cfg->owner, sizeof(cfg->owner), value);
            else if (strcmp(key, "repo") == 0)
                copy_field(cfg->repo, sizeof(cfg->repo), value);
            else if (strcmp(key, "token") == 0)
                copy_field(cfg->token, sizeof(cfg->token), value);
        }
        else if (strcmp(section, "games") == 0)
        {
            if (cfg->game_count >= CONFIG_MAX_GAMES)
                continue;

            ConfigGame *g = &cfg->games[cfg->game_count];
            copy_field(g->title_id_hex, sizeof(g->title_id_hex), key);
            copy_field(g->name, sizeof(g->name), value);
            g->title_id = strtoull(g->title_id_hex, NULL, 16);
            cfg->game_count++;
        }
    }

    fclose(f);

    if (cfg->api_base[0] == '\0' || cfg->owner[0] == '\0' || cfg->repo[0] == '\0' || cfg->token[0] == '\0')
    {
        if (err_out)
            snprintf(err_out, err_out_len, "config missing [remote] fields\n(api_base/owner/repo/token)");
        return false;
    }

    if (cfg->game_count == 0)
    {
        if (err_out)
            snprintf(err_out, err_out_len, "config has no [games] entries");
        return false;
    }

    return true;
}
