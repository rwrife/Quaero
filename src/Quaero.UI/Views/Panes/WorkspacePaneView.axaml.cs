using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Quaero.UI.Views.Panes;

public partial class WorkspacePaneView : UserControl
{
    public event Action? SearchModeRequested;
    public event Action? ChatModeRequested;
    public event Action? SubmitPrimaryInputRequested;

    public WorkspacePaneView()
    {
        InitializeComponent();
    }

    private void OnSearchModeChecked(object? sender, RoutedEventArgs e)
        => SearchModeRequested?.Invoke();

    private void OnChatModeChecked(object? sender, RoutedEventArgs e)
        => ChatModeRequested?.Invoke();

    private void OnSubmitPrimaryInputClicked(object? sender, RoutedEventArgs e)
        => SubmitPrimaryInputRequested?.Invoke();

    private void OnPrimaryInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        SubmitPrimaryInputRequested?.Invoke();
    }
}
