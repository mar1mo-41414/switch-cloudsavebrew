#include "save_export.h"
#include <switch.h>
#include <stdio.h>
#include <string.h>
#include <dirent.h>
#include <sys/stat.h>
#include <errno.h>

#define SAVE_MOUNT_NAME "save"
#define SAVE_MOUNT_PREFIX "save:"
#define PATH_BUF_LEN (FS_MAX_PATH + 1)

// mkdir -p, for the plain-file destination side (sdmc: or save:).
static void mkdir_p(const char *path)
{
    char tmp[PATH_BUF_LEN];
    snprintf(tmp, sizeof(tmp), "%s", path);

    for (char *p = tmp + 1; *p; p++)
    {
        if (*p == '/')
        {
            *p = '\0';
            mkdir(tmp, 0777);
            *p = '/';
        }
    }
    mkdir(tmp, 0777);
}

static bool copy_file(const char *srcPath, const char *dstPath)
{
    FILE *in = fopen(srcPath, "rb");
    if (!in)
        return false;

    FILE *out = fopen(dstPath, "wb");
    if (!out)
    {
        fclose(in);
        return false;
    }

    char buf[64 * 1024];
    size_t n;
    bool ok = true;
    while ((n = fread(buf, 1, sizeof(buf), in)) > 0)
    {
        if (fwrite(buf, 1, n, out) != n)
        {
            ok = false;
            break;
        }
    }
    if (ferror(in))
        ok = false;

    fclose(in);
    fclose(out);
    return ok;
}

// Recursively copies every file/dir from srcRoot into dstRoot, then removes
// anything under dstRoot that srcRoot doesn't have — same "mirror"
// semantics as the PC tool's DirectoryMirror, so the two stay compatible.
static bool mirror_copy(const char *srcRoot, const char *dstRoot, char *err, size_t errLen)
{
    mkdir_p(dstRoot);

    DIR *d = opendir(srcRoot);
    if (!d)
    {
        if (err)
            snprintf(err, errLen, "opendir failed: %s", srcRoot);
        return false;
    }

    struct dirent *entry;
    while ((entry = readdir(d)) != NULL)
    {
        if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0)
            continue;

        char srcPath[PATH_BUF_LEN], dstPath[PATH_BUF_LEN];
        snprintf(srcPath, sizeof(srcPath), "%s/%s", srcRoot, entry->d_name);
        snprintf(dstPath, sizeof(dstPath), "%s/%s", dstRoot, entry->d_name);

        struct stat st;
        if (stat(srcPath, &st) != 0)
            continue;

        if (S_ISDIR(st.st_mode))
        {
            if (!mirror_copy(srcPath, dstPath, err, errLen))
            {
                closedir(d);
                return false;
            }
        }
        else
        {
            if (!copy_file(srcPath, dstPath))
            {
                if (err)
                    snprintf(err, errLen, "copy failed: %s", entry->d_name);
                closedir(d);
                return false;
            }
        }
    }
    closedir(d);

    // Remove anything in dstRoot that srcRoot doesn't have.
    DIR *dd = opendir(dstRoot);
    if (dd)
    {
        while ((entry = readdir(dd)) != NULL)
        {
            if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0)
                continue;

            char srcPath[PATH_BUF_LEN], dstPath[PATH_BUF_LEN];
            snprintf(srcPath, sizeof(srcPath), "%s/%s", srcRoot, entry->d_name);
            snprintf(dstPath, sizeof(dstPath), "%s/%s", dstRoot, entry->d_name);

            struct stat st;
            if (stat(srcPath, &st) == 0)
                continue; // still present in source, keep it

            if (stat(dstPath, &st) == 0 && S_ISDIR(st.st_mode))
                fsdevDeleteDirectoryRecursively(dstPath);
            else
                remove(dstPath);
        }
        closedir(dd);
    }

    return true;
}

bool saveExport(uint64_t titleId, bool isDeviceSave, AccountUid uid, const char *stagingDir, char *err, size_t errLen)
{
    Result rc;
    if (isDeviceSave)
    {
        // No read-only variant exists for device saves — mount read-write
        // but we simply never write through it here.
        rc = fsdevMountDeviceSaveData(SAVE_MOUNT_NAME, titleId);
    }
    else
    {
        rc = fsdevMountSaveDataReadOnly(SAVE_MOUNT_NAME, titleId, uid);
    }

    if (R_FAILED(rc))
    {
        if (err)
            snprintf(err, errLen, "mount (ro) failed: 0x%x\n(no save data for this title/account?)", rc);
        return false;
    }

    bool ok = mirror_copy(SAVE_MOUNT_PREFIX, stagingDir, err, errLen);

    fsdevUnmountDevice(SAVE_MOUNT_NAME);
    return ok;
}

bool saveImport(uint64_t titleId, bool isDeviceSave, AccountUid uid, const char *stagingDir, char *err, size_t errLen)
{
    Result rc;
    if (isDeviceSave)
    {
        rc = fsdevMountDeviceSaveData(SAVE_MOUNT_NAME, titleId);
    }
    else
    {
        rc = fsdevMountSaveData(SAVE_MOUNT_NAME, titleId, uid);
    }

    if (R_FAILED(rc))
    {
        if (err)
            snprintf(err, errLen, "mount (rw) failed: 0x%x\n(has this title been run once yet?)", rc);
        return false;
    }

    bool ok = mirror_copy(stagingDir, SAVE_MOUNT_PREFIX, err, errLen);
    if (ok)
    {
        rc = fsdevCommitDevice(SAVE_MOUNT_NAME);
        if (R_FAILED(rc))
        {
            ok = false;
            if (err)
                snprintf(err, errLen, "commit failed: 0x%x", rc);
        }
    }

    fsdevUnmountDevice(SAVE_MOUNT_NAME);
    return ok;
}
