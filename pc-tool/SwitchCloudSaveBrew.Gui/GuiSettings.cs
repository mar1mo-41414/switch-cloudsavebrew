using System;
using System.IO;
using System.Text.Json;

namespace SwitchCloudSaveBrew.Gui;

// Just remembers the last-used config.yaml path (and chosen UI language)
// across runs so the user doesn't have to re-browse/re-select every time.
public sealed class GuiSettings
{
    public string? LastConfigPath { get; set; }

    // "en" or "ja" — independent of config.yaml's own language: field
    // (that one drives the CLI). Defaults to English when absent.
    public string Language { get; set; } = "en";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "switch-cloudsavebrew", "gui-settings.json");

    public static GuiSettings Load()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<GuiSettings>(json) ?? new GuiSettings();
        }
        catch (IOException)
        {
            return new GuiSettings();
        }
        catch (System.Text.Json.JsonException)
        {
            return new GuiSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
