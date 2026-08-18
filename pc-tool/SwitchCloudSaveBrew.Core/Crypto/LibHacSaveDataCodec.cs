using LibHac.Common.Keys;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Tools.FsSystem;

namespace SwitchCloudSaveBrew.Core.Crypto;

// NAX0 unwrap/wrap for SD-card save data, implemented the same way LibHac's
// own SwitchFs.OpenSdCard() does it: AesXtsFileSystem wraps a plain directory
// with per-file AES-XTS encryption, keyed by keySet.SdCardEncryptionKeys[0]
// (the "sd_card_save_key_source" derived key — index 0 confirmed by
// decompiling LibHac.Common.Keys.DefaultKeySet's key-name table) and a fixed
// 0x4000 block size (confirmed the same way, from SwitchFs.OpenSdCard).
//
// Per-file keys are only valid for the specific SD card the sd_seed was
// extracted from (NAX0 key derivation mixes in that seed) — re-encrypted
// saves can only be read back by the same physical console+SD card pairing
// that originally produced them. That's fine for this project's single-user,
// single-console design, but it's not a general-purpose "works on any
// console" converter.
//
// Encrypted-side paths must mirror the original SD path structure exactly
// (e.g. "<save-id>/0", "<save-id>/1", ...) because NAX0's per-file key wrap
// is derived in part from each file's path — this is why JKSV-style exports,
// which already mirror SD:/Nintendo/save/<save-id>/..., work as-is once
// pointed at the save-id directory.
public sealed class LibHacSaveDataCodec : ISaveDataCodec
{
    private const int Nax0BlockSize = 0x4000;

    private readonly KeySet _keySet;

    public LibHacSaveDataCodec(KeySet keySet)
    {
        _keySet = keySet;
    }

    public void DecryptDirectory(string encryptedSaveDir, string plainOutputDir)
    {
        Directory.CreateDirectory(plainOutputDir);

        using var source = new LocalFileSystem(encryptedSaveDir);
        using var encrypted = new AesXtsFileSystem(source, SaveKek(), Nax0BlockSize);
        using var dest = new LocalFileSystem(plainOutputDir);

        FileSystemExtensions.CopyDirectory(encrypted, dest, "/", "/", logger: null, CreateFileOptions.None)
            .ThrowIfFailure();
    }

    public void EncryptDirectory(string plainInputDir, string encryptedOutputDir)
    {
        Directory.CreateDirectory(encryptedOutputDir);

        using var source = new LocalFileSystem(plainInputDir);
        using var dest = new LocalFileSystem(encryptedOutputDir);
        using var encrypted = new AesXtsFileSystem(dest, SaveKek(), Nax0BlockSize);

        FileSystemExtensions.CopyDirectory(source, encrypted, "/", "/", logger: null, CreateFileOptions.None)
            .ThrowIfFailure();
    }

    private byte[] SaveKek() => _keySet.SdCardEncryptionKeys[0].DataRo.ToArray();
}
