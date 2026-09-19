#include <switch.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>
#include "config.h"
#include "save_sync.h"
#include "save_discovery.h"
#include "text_render.h"
#include "l10n.h"

static PadState s_pad;

typedef struct {
    ConfigGame game;
    bool from_config; // false = found via discoverGames(), not in config.ini
} MenuEntry;

#define MENU_MAX (CONFIG_MAX_GAMES + DISCOVERY_MAX_GAMES)
static MenuEntry s_menu[MENU_MAX];
static int s_menu_count;

static void build_menu(const Config *cfg)
{
    s_menu_count = 0;
    for (int i = 0; i < cfg->game_count && s_menu_count < MENU_MAX; i++)
    {
        s_menu[s_menu_count].game = cfg->games[i];
        s_menu[s_menu_count].from_config = true;
        s_menu_count++;
    }

    DiscoveredGame discovered[DISCOVERY_MAX_GAMES];
    int n = discoverGames(discovered, DISCOVERY_MAX_GAMES);
    for (int i = 0; i < n && s_menu_count < MENU_MAX; i++)
    {
        bool dup = false;
        for (int j = 0; j < s_menu_count; j++)
        {
            if (strcasecmp(s_menu[j].game.title_id_hex, discovered[i].title_id_hex) == 0)
            {
                dup = true;
                break;
            }
        }
        if (dup)
            continue;

        ConfigGame g = {0};
        snprintf(g.title_id_hex, sizeof(g.title_id_hex), "%s", discovered[i].title_id_hex);
        g.title_id = discovered[i].title_id;
        snprintf(g.name, sizeof(g.name), "%s", discovered[i].name);
        g.is_device_save = discovered[i].is_device_save;

        s_menu[s_menu_count].game = g;
        s_menu[s_menu_count].from_config = false;
        s_menu_count++;
    }
}

static void wait_for_a_or_b(u64 *outButton)
{
    while (appletMainLoop())
    {
        padUpdate(&s_pad);
        u64 kDown = padGetButtonsDown(&s_pad);
        if (kDown & (HidNpadButton_A | HidNpadButton_B))
        {
            *outButton = kDown;
            return;
        }
        textRenderPresent();
    }
    *outButton = 0;
}

static bool confirm_overwrite(const char *summary)
{
    textRenderPrintf(L("\n%s\n[A] Yes   [B] No\n", "\n%s\n[A] はい   [B] いいえ\n"), summary);
    textRenderPresent();

    u64 btn;
    wait_for_a_or_b(&btn);
    return (btn & HidNpadButton_A) != 0;
}

// count == 1 auto-selects with no prompt (the common case — nothing
// changes for a console with just one account). count > 1 shows a picker;
// [B] cancels back to the game list.
static bool pick_account(AccountUid *uids, char nicknames[][ACCOUNT_NICKNAME_LEN], int count, AccountUid *outUid)
{
    if (count == 1)
    {
        *outUid = uids[0];
        return true;
    }

    int cursor = 0;
    while (appletMainLoop())
    {
        textRenderClear();
        textRenderPrintf(L(
            "Multiple accounts have save data for this title.\nSelect one:\n\n",
            "このタイトルには複数アカウント分のセーブがあります。\n選んでください:\n\n"));
        for (int i = 0; i < count; i++)
            textRenderPrintf("%s %s\n", (i == cursor) ? ">" : " ", nicknames[i]);
        textRenderPrintf(L("\n[up/down] select  [A] choose  [B] cancel\n", "\n[上下] 選択  [A] 決定  [B] キャンセル\n"));
        textRenderPresent();

        padUpdate(&s_pad);
        u64 kDown = padGetButtonsDown(&s_pad);
        if (kDown & HidNpadButton_B)
            return false;
        if (kDown & HidNpadButton_A)
        {
            *outUid = uids[cursor];
            return true;
        }
        if (kDown & HidNpadButton_AnyDown)
            cursor = (cursor + 1) % count;
        if (kDown & HidNpadButton_AnyUp)
            cursor = (cursor - 1 + count) % count;
    }
    return false;
}

// Device saves have no account concept at all, so this is a no-op for
// those (uid is never used downstream in that case). For account saves,
// prefers accounts that already have save data for this title (so a
// console with just one relevant account never sees a picker); falls back
// to every account on the console when none do yet, e.g. pulling a title
// that's never been launched locally under any profile.
static bool resolve_account_for_game(const ConfigGame *game, AccountUid *outUid)
{
    if (game->is_device_save)
    {
        memset(outUid, 0, sizeof(*outUid));
        return true;
    }

    AccountUid uids[ACCOUNT_PICKER_MAX];
    char nicknames[ACCOUNT_PICKER_MAX][ACCOUNT_NICKNAME_LEN];

    int n = findAccountUidsForTitle(game->title_id, uids, nicknames, ACCOUNT_PICKER_MAX);
    if (n <= 0)
        n = listAllSystemAccounts(uids, nicknames, ACCOUNT_PICKER_MAX);
    if (n <= 0)
        return false;

    return pick_account(uids, nicknames, n, outUid);
}

static void show_accounts_screen(void)
{
    AccountUid uids[ACCOUNT_PICKER_MAX];
    char nicknames[ACCOUNT_PICKER_MAX][ACCOUNT_NICKNAME_LEN];
    int n = listAllSystemAccounts(uids, nicknames, ACCOUNT_PICKER_MAX);

    textRenderClear();
    textRenderPrintf(L("Accounts on this console:\n", "このコンソールのアカウント一覧:\n"));
    textRenderPrintf(L(
        "(paste the uid into config.yaml's accounts: switch_uid)\n\n",
        "(config.yamlのaccounts:のswitch_uidに貼り付けてください)\n\n"));
    if (n <= 0)
        textRenderPrintf(L("(none found)\n", "(見つかりませんでした)\n"));
    else
        for (int i = 0; i < n; i++)
            textRenderPrintf("%s\n  %016lX%016lX\n\n", nicknames[i], uids[i].uid[0], uids[i].uid[1]);
    textRenderPrintf(L("\n[B] back\n", "\n[B] 戻る\n"));
    textRenderPresent();

    while (appletMainLoop())
    {
        padUpdate(&s_pad);
        if (padGetButtonsDown(&s_pad) & HidNpadButton_B)
            return;
        textRenderPresent();
    }
}

static void run_action_menu(const Config *cfg, const ConfigGame *game, AccountUid uid)
{
    for (;;)
    {
        textRenderClear();
        textRenderPrintf("%s\n(%s%s)\n\n", game->name, game->title_id_hex,
               game->is_device_save ? L(", device save", "・デバイス型セーブ") : "");
        textRenderPrintf(L("[A] Push (device -> cloud)\n", "[A] Push (実機 → クラウド)\n"));
        textRenderPrintf(L("[Y] Pull (cloud -> device)\n", "[Y] Pull (クラウド → 実機)\n"));
        textRenderPrintf(L("[B] Back\n", "[B] 戻る\n"));
        textRenderPresent();

        while (appletMainLoop())
        {
            padUpdate(&s_pad);
            u64 kDown = padGetButtonsDown(&s_pad);

            if (kDown & HidNpadButton_B)
                return;

            if (kDown & HidNpadButton_A)
            {
                textRenderPrintf(L("\nExporting + pushing...\n", "\nエクスポートしてpushしています...\n"));
                textRenderPresent();

                char msg[256];
                SyncResult r = syncPush(cfg, game, uid, msg, sizeof(msg));
                textRenderPrintf("%s\n", msg);
                textRenderPrintf((r == SYNC_ERROR) ? L("FAILED\n", "失敗しました\n") : L("done.\n", "完了しました\n"));
                textRenderPrintf(L("\n[A/B] continue\n", "\n[A/B] 続ける\n"));
                textRenderPresent();
                u64 b;
                wait_for_a_or_b(&b);
                break;
            }

            if (kDown & HidNpadButton_Y)
            {
                textRenderPrintf(L("\nChecking cloud...\n", "\nクラウドを確認しています...\n"));
                textRenderPresent();

                char msg[256];
                SyncResult r = syncPull(cfg, game, uid, confirm_overwrite, msg, sizeof(msg));
                textRenderPrintf("%s\n", msg);
                textRenderPrintf((r == SYNC_ERROR) ? L("FAILED\n", "失敗しました\n") : L("done.\n", "完了しました\n"));
                textRenderPrintf(L("\n[A/B] continue\n", "\n[A/B] 続ける\n"));
                textRenderPresent();
                u64 b;
                wait_for_a_or_b(&b);
                break;
            }

            textRenderPresent();
        }
    }
}

int main(int argc, char *argv[])
{
    if (!textRenderInit())
    {
        // Fall back to the plain console if the shared font/framebuffer
        // setup fails for some reason — ASCII-only, but better than a
        // silent crash. g_language is still LANG_EN at this point (config
        // hasn't loaded yet), so no need to pick between languages here.
        consoleInit(NULL);
        printf("text renderer init failed - falling back to plain console\n"
               "(Japanese text will not display correctly)\n\n[+] exit\n");
        consoleUpdate(NULL);
        while (appletMainLoop())
        {
            padConfigureInput(1, HidNpadStyleSet_NpadStandard);
            padInitializeDefault(&s_pad);
            padUpdate(&s_pad);
            if (padGetButtonsDown(&s_pad) & HidNpadButton_Plus)
                break;
            consoleUpdate(NULL);
        }
        consoleExit(NULL);
        return 1;
    }

    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&s_pad);

    timeInitialize();
    socketInitializeDefault();

    Config cfg;
    char err[256];
    bool haveConfig = configLoad(&cfg, err, sizeof(err));

    if (haveConfig)
    {
        g_language = (strcasecmp(cfg.language, "ja") == 0 || strcasecmp(cfg.language, "jp") == 0)
            ? LANG_JA : LANG_EN;

        textRenderPrintf(L(
            "Switch-CloudSaveBrew\n\nScanning save data on this console...\n",
            "Switch-CloudSaveBrew\n\nこのコンソール上のセーブデータをスキャン中...\n"));
        textRenderPresent();
        build_menu(&cfg);
    }

    int cursor = 0;

    while (appletMainLoop())
    {
        textRenderClear();
        textRenderPrintf("Switch-CloudSaveBrew\n\n");

        if (!haveConfig)
        {
            textRenderPrintf(L("config error:\n%s\n\n", "設定エラー:\n%s\n\n"), err);
            textRenderPrintf(L("[+] exit\n", "[+] 終了\n"));
            textRenderPresent();

            padUpdate(&s_pad);
            if (padGetButtonsDown(&s_pad) & HidNpadButton_Plus)
                break;
            continue;
        }

        if (s_menu_count == 0)
        {
            textRenderPrintf(L(
                "No games configured, and no save data found on this\n"
                "console to auto-detect. Add entries under [games] in\n"
                "config.ini, or run a game once first.\n\n[+] exit\n",
                "ゲームが設定されておらず、自動検出できるセーブ\n"
                "データも見つかりませんでした。config.iniの\n"
                "[games]に追加するか、一度ゲームを起動してから\n"
                "試してください。\n\n[+] 終了\n"));
            textRenderPresent();

            padUpdate(&s_pad);
            if (padGetButtonsDown(&s_pad) & HidNpadButton_Plus)
                break;
            continue;
        }

        for (int i = 0; i < s_menu_count; i++)
        {
            textRenderPrintf("%s %s%s\n", (i == cursor) ? ">" : " ",
                   s_menu[i].from_config ? "" : L("[auto] ", "[自動] "), s_menu[i].game.name);
        }
        textRenderPrintf(L(
            "\n[up/down] select  [A] open  [X] accounts  [-] %s  [+] exit\n",
            "\n[上下] 選択  [A] 開く  [X] アカウント一覧  [-] %s  [+] 終了\n"),
            g_language == LANG_JA ? "English" : "日本語");
        textRenderPresent();

        padUpdate(&s_pad);
        u64 kDown = padGetButtonsDown(&s_pad);

        if (kDown & HidNpadButton_Plus)
            break;
        if (kDown & HidNpadButton_AnyDown)
            cursor = (cursor + 1) % s_menu_count;
        if (kDown & HidNpadButton_AnyUp)
            cursor = (cursor - 1 + s_menu_count) % s_menu_count;
        if (kDown & HidNpadButton_Minus)
            g_language = (g_language == LANG_JA) ? LANG_EN : LANG_JA;
        if (kDown & HidNpadButton_X)
            show_accounts_screen();
        if (kDown & HidNpadButton_A)
        {
            AccountUid uid;
            if (resolve_account_for_game(&s_menu[cursor].game, &uid))
                run_action_menu(&cfg, &s_menu[cursor].game, uid);
        }
    }

    socketExit();
    textRenderExit();
    return 0;
}
