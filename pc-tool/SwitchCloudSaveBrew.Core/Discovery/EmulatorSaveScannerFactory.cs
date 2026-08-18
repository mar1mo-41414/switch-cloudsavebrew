namespace SwitchCloudSaveBrew.Core.Discovery;

public static class EmulatorSaveScannerFactory
{
    public static IEmulatorSaveScanner Get(string emulator) => emulator.ToLowerInvariant() switch
    {
        "ryujinx" => new RyujinxSaveScanner(),
        "eden" => new EdenSaveScanner(),
        _ => throw new ArgumentException(
            $"no auto-detect scanner for emulator '{emulator}' (supported: ryujinx, eden)"),
    };
}
