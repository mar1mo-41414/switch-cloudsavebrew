using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SwitchCloudSaveBrew.Gui;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string message) : this()
    {
        this.FindControl<TextBlock>("MessageText")!.Text = message;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
