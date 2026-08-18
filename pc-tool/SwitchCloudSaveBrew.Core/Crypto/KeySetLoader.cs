using LibHac.Common.Keys;
using SwitchCloudSaveBrew.Core.Config;

namespace SwitchCloudSaveBrew.Core.Crypto;

public static class KeySetLoader
{
    public static KeySet Load(KeysConfig config)
    {
        var prodKeysPath = PathUtil.Expand(config.ProdKeysPath);
        if (!File.Exists(prodKeysPath))
            throw new FileNotFoundException($"prod.keys not found: {prodKeysPath}", prodKeysPath);

        var keySet = KeySet.CreateDefaultKeySet();
        ExternalKeyReader.ReadKeyFile(keySet, prodKeysPath, null, null, logger: null);

        if (string.IsNullOrWhiteSpace(config.SdSeed))
            throw new InvalidOperationException("keys.sd_seed is required to unwrap NAX0-encrypted SD save data");

        keySet.SetSdSeed(ParseHex(config.SdSeed));
        keySet.DeriveSdCardKeys();

        return keySet;
    }

    private static byte[] ParseHex(string hex)
    {
        hex = hex.Trim();
        if (hex.Length % 2 != 0)
            throw new FormatException("sd_seed must be an even-length hex string");

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

        return bytes;
    }
}
