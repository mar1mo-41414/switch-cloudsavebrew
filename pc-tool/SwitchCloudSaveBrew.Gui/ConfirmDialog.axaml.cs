using Avalonia.Controls;
using Avalonia.Interactivity;
using SwitchCloudSaveBrew.Core;

namespace SwitchCloudSaveBrew.Gui;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();

        // L.Current is already set by MainWindow before this dialog is
        // ever constructed, so just apply it once here.
        Title = L.Pick("Confirm overwrite", "上書きの確認");
        this.FindControl<Button>("CancelButton")!.Content = L.Pick("Cancel", "キャンセル");
        this.FindControl<Button>("OverwriteButton")!.Content = L.Pick("Overwrite", "上書き");
    }

    public ConfirmDialog(string message) : this()
    {
        this.FindControl<TextBlock>("MessageText")!.Text = message;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
