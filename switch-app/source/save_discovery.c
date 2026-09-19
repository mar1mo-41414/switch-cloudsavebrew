#include "save_discovery.h"
#include <switch.h>
#include <string.h>
#include <stdio.h>

static bool already_seen(const DiscoveredGame *out, int count, uint64_t titleId)
{
    for (int i = 0; i < count; i++)
        if (out[i].title_id == titleId)
            return true;
    return false;
}

static void resolve_name(uint64_t titleId, char *nameOut, size_t nameOutLen)
{
    // Fallback if ns lookup fails, or the NACP has no name at all for the
    // system's current language.
    snprintf(nameOut, nameOutLen, "%016lX", titleId);

    static NsApplicationControlData controlData;
    u64 actualSize = 0;
    Result rc = nsGetApplicationControlData(
        NsApplicationControlSource_CacheOnly, titleId, &controlData, sizeof(controlData), &actualSize);
    if (R_FAILED(rc))
        return;

    // text_render.c rasterizes glyphs from the console's own shared system
    // font (the same one Nintendo's own Japanese UI uses), so unlike
    // consoleInit's built-in ASCII-only bitmap font, non-Latin NACP names
    // (Japanese, etc.) render correctly here — just use whatever the
    // console's language setting picked directly, no ASCII fallback
    // needed.
    NacpLanguageEntry *lang = NULL;
    nacpGetLanguageEntry(&controlData.nacp, &lang);

    if (lang && lang->name[0] != '\0')
        snprintf(nameOut, nameOutLen, "%s", lang->name);
}

int discoverGames(DiscoveredGame *out, int maxGames)
{
    Result rc = nsInitialize();
    if (R_FAILED(rc))
        return -1;

    FsSaveDataInfoReader reader;
    rc = fsOpenSaveDataInfoReader(&reader, FsSaveDataSpaceId_All);
    if (R_FAILED(rc))
    {
        nsExit();
        return -1;
    }

    int count = 0;
    FsSaveDataInfo buf[32];
    for (;;)
    {
        s64 total = 0;
        rc = fsSaveDataInfoReaderRead(&reader, buf, 32, &total);
        if (R_FAILED(rc) || total == 0)
            break;

        for (s64 i = 0; i < total && count < maxGames; i++)
        {
            bool isAccount = buf[i].save_data_type == FsSaveDataType_Account;
            bool isDevice = buf[i].save_data_type == FsSaveDataType_Device;
            if (!isAccount && !isDevice)
                continue; // skips Bcat/SystemBcat/System/Cache/etc — see header comment

            uint64_t titleId = buf[i].application_id;
            if (already_seen(out, count, titleId))
                continue;

            out[count].title_id = titleId;
            snprintf(out[count].title_id_hex, sizeof(out[count].title_id_hex), "%016lX", titleId);
            out[count].is_device_save = isDevice;
            resolve_name(titleId, out[count].name, sizeof(out[count].name));
            count++;
        }

        if (total < 32)
            break;
    }

    fsSaveDataInfoReaderClose(&reader);
    nsExit();
    return count;
}

// Falls back to the hex uid if account service lookup fails or the profile
// has no nickname set — the caller (a picker menu) still needs something
// non-empty to display either way.
static void resolve_nickname(AccountUid uid, char *out, size_t outLen)
{
    snprintf(out, outLen, "%016lX%016lX", uid.uid[0], uid.uid[1]);

    AccountProfile profile;
    if (R_FAILED(accountGetProfile(&profile, uid)))
        return;

    AccountProfileBase base;
    if (R_SUCCEEDED(accountProfileGet(&profile, NULL, &base)) && base.nickname[0] != '\0')
        snprintf(out, outLen, "%s", base.nickname);

    accountProfileClose(&profile);
}

static bool uid_equal(AccountUid a, AccountUid b)
{
    return a.uid[0] == b.uid[0] && a.uid[1] == b.uid[1];
}

int findAccountUidsForTitle(uint64_t titleId, AccountUid *outUids, char outNicknames[][ACCOUNT_NICKNAME_LEN], int max)
{
    FsSaveDataInfoReader reader;
    Result rc = fsOpenSaveDataInfoReader(&reader, FsSaveDataSpaceId_All);
    if (R_FAILED(rc))
        return -1;

    Result accRc = accountInitialize(AccountServiceType_Application);

    int count = 0;
    FsSaveDataInfo buf[32];
    for (;;)
    {
        s64 total = 0;
        rc = fsSaveDataInfoReaderRead(&reader, buf, 32, &total);
        if (R_FAILED(rc) || total == 0)
            break;

        for (s64 i = 0; i < total && count < max; i++)
        {
            if (buf[i].save_data_type != FsSaveDataType_Account || buf[i].application_id != titleId)
                continue;

            bool dup = false;
            for (int j = 0; j < count; j++)
                if (uid_equal(outUids[j], buf[i].uid)) { dup = true; break; }
            if (dup)
                continue;

            outUids[count] = buf[i].uid;
            if (R_SUCCEEDED(accRc))
                resolve_nickname(buf[i].uid, outNicknames[count], ACCOUNT_NICKNAME_LEN);
            else
                snprintf(outNicknames[count], ACCOUNT_NICKNAME_LEN, "%016lX%016lX", buf[i].uid.uid[0], buf[i].uid.uid[1]);
            count++;
        }

        if (total < 32)
            break;
    }

    fsSaveDataInfoReaderClose(&reader);
    if (R_SUCCEEDED(accRc))
        accountExit();
    return count;
}

int listAllSystemAccounts(AccountUid *outUids, char outNicknames[][ACCOUNT_NICKNAME_LEN], int max)
{
    Result rc = accountInitialize(AccountServiceType_Application);
    if (R_FAILED(rc))
        return -1;

    s32 total = 0;
    rc = accountListAllUsers(outUids, max, &total);
    if (R_FAILED(rc))
    {
        accountExit();
        return -1;
    }
    if (total > max)
        total = max;

    for (s32 i = 0; i < total; i++)
        resolve_nickname(outUids[i], outNicknames[i], ACCOUNT_NICKNAME_LEN);

    accountExit();
    return total;
}
