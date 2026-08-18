namespace SwitchCloudSaveBrew.Core.Crypto;

// Converts between the raw NAX0-wrapped save tree as exported to the SD card
// (what the Switch homebrew app pushes to the repo unmodified — e.g. a mirror
// of SD:/Nintendo/save/<save-id>/...) and a plain save-data tree an emulator
// can read/write directly. Operates on directories, not single files, because
// a Switch save is itself a small directory tree (journal + duplex copies),
// and NAX0's per-file key derivation is keyed in part by each file's path
// relative to the save root.
public interface ISaveDataCodec
{
    void DecryptDirectory(string encryptedSaveDir, string plainOutputDir);
    void EncryptDirectory(string plainInputDir, string encryptedOutputDir);
}
