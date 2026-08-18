#pragma once
#include <stddef.h>

// Same algorithm as the PC tool's Sync/SaveHasher.cs (sorted relative paths
// + per-file SHA256, then hashed together) so state.json's ContentHash
// means the same thing regardless of which side wrote it. Writes a 64-char
// lowercase hex string (+ NUL) to hexOut, which must be at least 65 bytes.
void saveHashDirectory(const char *rootDir, char *hexOut);
