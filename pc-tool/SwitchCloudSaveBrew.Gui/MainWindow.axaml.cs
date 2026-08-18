using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SwitchCloudSaveBrew.Core;
using SwitchCloudSaveBrew.Core.Config;
using SwitchCloudSaveBrew.Core.Discovery;
using SwitchCloudSaveBrew.Core.Sync;

namespace SwitchCloudSaveBrew.Gui;

public sealed class GameTargetRow
{
    public required GameConfig Game { get; init; }
    public required TargetConfig Target { get; init; }
    public override string ToString() => $"{Game.Name}  [{Target.Emulator}/{Target.Os}]";
}

public sealed class ScanRow
{
    public required DetectedSave Save { get; init; }
    public required string Emulator { get; init; }

    public override string ToString()
    {
        var kind = Save.IsFile ? "[file]" : Save.IsDeviceSave ? "[dir/device]" : "[dir]";
        var name = string.IsNullOrEmpty(Save.Name) ? "(unknown title)" : Save.Name;
        var uid = Save.LocalAccountUid is null ? "" : $"  uid:{Save.LocalAccountUid}";
        return $"{Save.TitleIdHex}  {kind}  {name}{uid}";
    }
}

public sealed class CloudRow
{
    public required CloudSaveEntry Entry { get; init; }

    public override string ToString() => Entry.State is null
        ? $"{Entry.TitleId}  {Entry.Name}  [{Entry.AccountName}]  (no state.json)"
        : $"{Entry.TitleId}  {Entry.Name}  [{Entry.AccountName}]  gen {Entry.State.Generation}, {Entry.State.UpdatedByDevice} @ {Entry.State.UpdatedAtUtc:u}";
}

public partial class MainWindow : Window
{
    private AppConfig? _config;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        var cwdConfigPath = FindConfigInCurrentDirectory();
        if (cwdConfigPath is not null)
        {
            ConfigPathBox.Text = cwdConfigPath;
            LoadConfig(cwdConfigPath);
            return;
        }

        var settings = GuiSettings.Load();
        if (!string.IsNullOrWhiteSpace(settings.LastConfigPath))
        {
            ConfigPathBox.Text = settings.LastConfigPath;
            LoadConfig(settings.LastConfigPath);
        }
    }

    // Lets you just drop config.yaml next to the exe and double-click it —
    // no browsing needed. Only checked at startup; explicit Load/Browse
    // always wins after that.
    private static string? FindConfigInCurrentDirectory()
    {
        foreach (var name in new[] { "config.yaml", "config.yml" })
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select config.yaml",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("YAML") { Patterns = ["*.yaml", "*.yml"] }],
        });

        if (files.Count > 0)
            ConfigPathBox.Text = files[0].Path.LocalPath;
    }

    private void OnLoadClick(object? sender, RoutedEventArgs e) => LoadConfig(ConfigPathBox.Text ?? "");

    private void LoadConfig(string path)
    {
        try
        {
            _config = ConfigLoader.Load(path);

            var rows = new ObservableCollection<GameTargetRow>();
            foreach (var game in _config.Games)
            foreach (var target in game.Targets)
                rows.Add(new GameTargetRow { Game = game, Target = target });

            GameList.ItemsSource = rows;
            Log($"loaded {_config.Games.Count} game(s), {rows.Count} target(s) — remote {_config.Remote.GitUrl}");

            new GuiSettings { LastConfigPath = path }.Save();
        }
        catch (Exception ex)
        {
            Log($"config load failed: {ex.Message}");
        }
    }

    private void OnGameSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = GameList.SelectedItem is not null && !_busy;
        PushButton.IsEnabled = selected;
        PullButton.IsEnabled = selected;
    }

    private async void OnPushClick(object? sender, RoutedEventArgs e)
    {
        if (_config is null || GameList.SelectedItem is not GameTargetRow row)
            return;

        await RunBusyAsync(async () =>
        {
            Log($"pushing {row.Game.Name} [{row.Target.Emulator}]...");
            var result = await Task.Run(() => SaveSyncService.Push(_config, row.Game.TitleId, row.Target));
            Log($"push: {result.Message}");
        });
    }

    private async void OnPullClick(object? sender, RoutedEventArgs e)
    {
        if (_config is null || GameList.SelectedItem is not GameTargetRow row)
            return;

        await RunBusyAsync(async () =>
        {
            Log($"checking cloud for {row.Game.Name} [{row.Target.Emulator}]...");
            var result = await Task.Run(() => SaveSyncService.Pull(_config, row.Game.TitleId, row.Target, ShowConfirmDialog));
            Log($"pull: {result.Message}");
        });
    }

    // -- Scan tab: auto-detect saves under an emulator's root dir, no
    // config.yaml entry required (mirrors the CLI's `scan list/push/pull`).

    private async void OnScanBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select emulator root directory",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
            ScanRootBox.Text = folders[0].Path.LocalPath;
    }

    private string SelectedScanEmulator =>
        (ScanEmulatorBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ryujinx";

    private void OnScanClick(object? sender, RoutedEventArgs e)
    {
        var rootDir = ScanRootBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(rootDir))
        {
            Log("scan: enter an emulator root directory first");
            return;
        }

        try
        {
            var emulator = SelectedScanEmulator;
            var detected = EmulatorSaveScannerFactory.Get(emulator).Scan(PathUtil.Expand(rootDir));

            foreach (var save in detected)
            {
                var known = _config?.Games.FirstOrDefault(g =>
                    g.TitleId.Equals(save.TitleIdHex, StringComparison.OrdinalIgnoreCase));
                if (known is not null)
                    save.Name = known.Name;
            }

            var rows = new ObservableCollection<ScanRow>(
                detected.Select(d => new ScanRow { Save = d, Emulator = emulator }));
            ScanList.ItemsSource = rows;
            Log($"scan: {rows.Count} save(s) detected under {rootDir}");
        }
        catch (Exception ex)
        {
            Log($"scan failed: {ex.Message}");
        }
    }

    private void OnScanSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = _config is not null && ScanList.SelectedItem is not null && !_busy;
        ScanPushButton.IsEnabled = selected;
        ScanPullButton.IsEnabled = selected;
    }

    private async void OnScanPushClick(object? sender, RoutedEventArgs e)
    {
        if (_config is null || ScanList.SelectedItem is not ScanRow row)
            return;

        var target = ToTargetConfig(row);
        await RunBusyAsync(async () =>
        {
            Log($"pushing {row.Save.TitleIdHex} [{row.Emulator}]...");
            var result = await Task.Run(() => SaveSyncService.Push(_config, row.Save.TitleIdHex, target));
            Log($"push: {result.Message}");
        });
    }

    private async void OnScanPullClick(object? sender, RoutedEventArgs e)
    {
        if (_config is null || ScanList.SelectedItem is not ScanRow row)
            return;

        var target = ToTargetConfig(row);
        await RunBusyAsync(async () =>
        {
            Log($"checking cloud for {row.Save.TitleIdHex} [{row.Emulator}]...");
            var result = await Task.Run(() => SaveSyncService.Pull(_config, row.Save.TitleIdHex, target, ShowConfirmDialog));
            Log($"pull: {result.Message}");
        });
    }

    private static TargetConfig ToTargetConfig(ScanRow row) => new()
    {
        Emulator = row.Emulator,
        Os = Environment.OSVersion.Platform.ToString(),
        SavePath = row.Save.LocalPath,
        IsFile = row.Save.IsFile,
        IsDuplex = row.Save.IsDuplex,
        IsDeviceSave = row.Save.IsDeviceSave,
    };

    // -- Cloud tab: read-only view of what's currently in the switch-savedata repo.

    private async void OnCloudRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (_config is null)
        {
            Log("cloud: load a config.yaml first");
            return;
        }

        await RunBusyAsync(async () =>
        {
            Log("refreshing cloud listing...");
            var entries = await Task.Run(() => CloudListingService.List(_config));
            CloudList.ItemsSource = new ObservableCollection<CloudRow>(
                entries.Select(e => new CloudRow { Entry = e }));
            Log($"cloud: {entries.Count} save(s) in repo");
        });
    }

    // Called from the background sync task (see Task.Run above) — dialog
    // has to be shown on the UI thread, and the sync call needs to block
    // until the user answers, so this hops to the UI thread and waits.
    private bool ShowConfirmDialog(PullPreview preview)
    {
        var message = $"Overwrite\n\"{preview.SavePath}\"\nwith cloud gen {preview.RemoteGeneration} " +
                      $"(from {preview.UpdatedByDevice} at {preview.UpdatedAtUtc:u})?";

        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ConfirmDialog(message);
            return await dialog.ShowDialog<bool>(this);
        }).GetAwaiter().GetResult();
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        _busy = true;
        PushButton.IsEnabled = false;
        PullButton.IsEnabled = false;
        ScanPushButton.IsEnabled = false;
        ScanPullButton.IsEnabled = false;
        StatusText.Text = "Working…";
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log($"error: {ex.Message}");
        }
        finally
        {
            _busy = false;
            StatusText.Text = "Ready";
            OnGameSelectionChanged(this, null!);
            OnScanSelectionChanged(this, null!);
        }
    }

    private void Log(string line)
    {
        LogBox.Text = $"{LogBox.Text}{(string.IsNullOrEmpty(LogBox.Text) ? "" : "\n")}{DateTime.Now:HH:mm:ss}  {line}";
    }
}
