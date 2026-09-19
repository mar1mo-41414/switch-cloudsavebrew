namespace SwitchCloudSaveBrew.Core.Discovery;

public static class EmulatorSaveScannerFactory
{
    public static IEmulatorSaveScanner Get(string emulator) => emulator.ToLowerInvariant() switch
    {
        "ryujinx" => new RyujinxSaveScanner(),
        "eden" => new EdenSaveScanner(),
        _ => throw new ArgumentException(L.Pick(
            $"no auto-detect scanner for emulator '{emulator}' (supported: ryujinx, eden)",
            $"エミュレータ '{emulator}' には自動検出スキャナーがありません (対応: ryujinx, eden)")),
    };
}
