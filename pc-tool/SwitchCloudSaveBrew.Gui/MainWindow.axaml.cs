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
        var kind = Save.IsFile
            ? L.Pick("[file]", "[ファイル]")
            : Save.IsDeviceSave
                ? L.Pick("[dir/device]", "[ディレクトリ/デバイス]")
                : L.Pick("[dir]", "[ディレクトリ]");
        var name = string.IsNullOrEmpty(Save.Name) ? L.Pick("(unknown title)", "(不明なタイトル)") : Save.Name;
        var uid = Save.LocalAccountUid is null ? "" : $"  uid:{Save.LocalAccountUid}";
        return $"{Save.TitleIdHex}  {kind}  {name}{uid}";
    }
}

public sealed class CloudRow
{
    public required CloudSaveEntry Entry { get; init; }

    public override string ToString() => Entry.State is null
        ? $"{Entry.TitleId}  {Entry.Name}  [{Entry.AccountName}]  {L.Pick("(no state.json)", "(state.jsonなし)")}"
        : L.Pick(
            $"{Entry.TitleId}  {Entry.Name}  [{Entry.AccountName}]  gen {Entry.State.Generation}, {Entry.State.UpdatedByDevice} @ {Entry.State.UpdatedAtUtc:u}",
            $"{Entry.TitleId}  {Entry.Name}  [{Entry.AccountName}]  gen {Entry.State.Generation}, 最終更新: {Entry.State.UpdatedByDevice} ({Entry.State.UpdatedAtUtc:u})");
}

public partial class MainWindow : Window
{
    private AppConfig? _config;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        var settings = GuiSettings.Load();
        var lang = L.Parse(settings.Language);
        LanguageBox.SelectedIndex = lang == Language.Ja ? 1 : 0;
        ApplyLanguage(lang);

        // Wired up here rather than in XAML: the ComboBox's own
        // SelectedIndex="0" in the markup fires SelectionChanged during
        // InitializeComponent() itself (mid-parse, before later-declared
        // controls like the tabs exist yet), which would NRE inside
        // ApplyLanguage() if this handler were already attached at that
        // point.
        LanguageBox.SelectionChanged += OnLanguageChanged;

        var cwdConfigPath = FindConfigInCurrentDirectory();
        if (cwdConfigPath is not null)
        {
            ConfigPathBox.Text = cwdConfigPath;
            LoadConfig(cwdConfigPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.LastConfigPath))
        {
            ConfigPathBox.Text = settings.LastConfigPath;
            LoadConfig(settings.LastConfigPath);
        }
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        var lang = LanguageBox.SelectedIndex == 1 ? Language.Ja : Language.En;
        ApplyLanguage(lang);

        var settings = GuiSettings.Load();
        settings.Language = lang == Language.Ja ? "ja" : "en";
        settings.Save();
    }

    // Sets L.Current (so Core's own messages/exceptions and this window's
    // Log() calls pick it up) and pushes the translated text into every
    // static UI element. Already-populated list rows (GameList/ScanList/
    // CloudList) keep whatever language they were built in until the next
    // Load/Scan/Refresh — not worth a live-refresh mechanism for a
    // personal tool with three short lists.
    private void ApplyLanguage(Language lang)
    {
        L.Current = lang;

        ConfigPathBox.PlaceholderText = L.Pick("path to config.yaml", "config.yamlのパス");
        BrowseButton.Content = L.Pick("Browse…", "参照…");
        LoadButton.Content = L.Pick("Load", "読み込み");
        StatusText.Text = _busy ? L.Pick("Working…", "処理中…") : L.Pick("Ready", "準備完了");

        GamesTab.Header = L.Pick("Games", "ゲーム");
        PushButton.Content = L.Pick("Push (device → cloud)", "Push (実機 → クラウド)");
        PullButton.Content = L.Pick("Pull (cloud → device)", "Pull (クラウド → 実機)");

        ScanTab.Header = L.Pick("Scan", "自動検出");
        ScanRootBox.PlaceholderText = L.Pick("emulator root directory", "エミュレータのルートディレクトリ");
        ScanBrowseButton.Content = L.Pick("Browse…", "参照…");
        ScanButton.Content = L.Pick("Scan", "検出");
        ScanPushButton.Content = L.Pick("Push (device → cloud)", "Push (実機 → クラウド)");
        ScanPullButton.Content = L.Pick("Pull (cloud → device)", "Pull (クラウド → 実機)");

        CloudTab.Header = L.Pick("Cloud", "クラウド");
        CloudRefreshButton.Content = L.Pick("Refresh", "更新");
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
            Title = L.Pick("Select config.yaml", "config.yamlを選択"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("YAML") { Patterns = ["*.yaml", "*.yml"] }],
        });

        if (files.Count > 0)
            ConfigPathBox.Text = files[0].Path.LocalPath;
    }

    private void OnLoadClick(object? sender, RoutedEventArgs e) => LoadConfig(ConfigPathBox.Text ?? "");

    private void LoadConfig(string path)
    {
        // ConfigLoader.Load() sets L.Current from config.yaml's own
        // language: field as a side effect (for the CLI's benefit) — the
        // GUI's choice should win instead, so restore it right after.
        var guiLang = L.Current;
        try
        {
            _config = ConfigLoader.Load(path);
            L.Current = guiLang;

            var rows = new ObservableCollection<GameTargetRow>();
            foreach (var game in _config.Games)
            foreach (var target in game.Targets)
                rows.Add(new GameTargetRow { Game = game, Target = target });

            GameList.ItemsSource = rows;
            Log(L.Pick(
                $"loaded {_config.Games.Count} game(s), {rows.Count} target(s) — remote {_config.Remote.GitUrl}",
                $"{_config.Games.Count}件のゲーム、{rows.Count}件のターゲットを読み込みました — remote {_config.Remote.GitUrl}"));

            var settings = GuiSettings.Load();
            settings.LastConfigPath = path;
            settings.Save();
        }
        catch (Exception ex)
        {
            L.Current = guiLang;
            Log(L.Pick($"config load failed: {ex.Message}", $"config読み込み失敗: {ex.Message}"));
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
            Log(L.Pick($"pushing {row.Game.Name} [{row.Target.Emulator}]...", $"{row.Game.Name} [{row.Target.Emulator}] をpushしています..."));
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
            Log(L.Pick($"checking cloud for {row.Game.Name} [{row.Target.Emulator}]...", $"{row.Game.Name} [{row.Target.Emulator}] のクラウドを確認しています..."));
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
            Title = L.Pick("Select emulator root directory", "エミュレータのルートディレクトリを選択"),
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
            Log(L.Pick("scan: enter an emulator root directory first", "scan: 先にエミュレータのルートディレクトリを入力してください"));
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
            Log(L.Pick($"scan: {rows.Count} save(s) detected under {rootDir}", $"scan: {rootDir} 配下で{rows.Count}件のセーブを検出しました"));
        }
        catch (Exception ex)
        {
            Log(L.Pick($"scan failed: {ex.Message}", $"scan失敗: {ex.Message}"));
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
            Log(L.Pick($"pushing {row.Save.TitleIdHex} [{row.Emulator}]...", $"{row.Save.TitleIdHex} [{row.Emulator}] をpushしています..."));
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
            Log(L.Pick($"checking cloud for {row.Save.TitleIdHex} [{row.Emulator}]...", $"{row.Save.TitleIdHex} [{row.Emulator}] のクラウドを確認しています..."));
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
            Log(L.Pick("cloud: load a config.yaml first", "cloud: 先にconfig.yamlを読み込んでください"));
            return;
        }

        await RunBusyAsync(async () =>
        {
            Log(L.Pick("refreshing cloud listing...", "クラウドの一覧を更新しています..."));
            var entries = await Task.Run(() => CloudListingService.List(_config));
            CloudList.ItemsSource = new ObservableCollection<CloudRow>(
                entries.Select(e => new CloudRow { Entry = e }));
            Log(L.Pick($"cloud: {entries.Count} save(s) in repo", $"cloud: リポジトリに{entries.Count}件のセーブがあります"));
        });
    }

    // Called from the background sync task (see Task.Run above) — dialog
    // has to be shown on the UI thread, and the sync call needs to block
    // until the user answers, so this hops to the UI thread and waits.
    private bool ShowConfirmDialog(PullPreview preview)
    {
        var message = L.Pick(
            $"Overwrite\n\"{preview.SavePath}\"\nwith cloud gen {preview.RemoteGeneration} " +
            $"(from {preview.UpdatedByDevice} at {preview.UpdatedAtUtc:u})?",
            $"\"{preview.SavePath}\"\nをクラウドのgen {preview.RemoteGeneration}で上書きします " +
            $"({preview.UpdatedByDevice} が {preview.UpdatedAtUtc:u} に更新)。よろしいですか?");

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
        StatusText.Text = L.Pick("Working…", "処理中…");
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log(L.Pick($"error: {ex.Message}", $"エラー: {ex.Message}"));
        }
        finally
        {
            _busy = false;
            StatusText.Text = L.Pick("Ready", "準備完了");
            OnGameSelectionChanged(this, null!);
            OnScanSelectionChanged(this, null!);
        }
    }

    private void Log(string line)
    {
        LogBox.Text = $"{LogBox.Text}{(string.IsNullOrEmpty(LogBox.Text) ? "" : "\n")}{DateTime.Now:HH:mm:ss}  {line}";
    }
}
