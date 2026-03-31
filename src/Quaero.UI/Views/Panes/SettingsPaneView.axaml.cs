using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Quaero.UI.Views.Panes;

public partial class SettingsPaneView : UserControl
{
    public event Action? ToggleIndexerRequested;
    public event Action? CompactRequested;
    public event Action? IndexAllRequested;
    public event Action? RefreshRequested;
    public event Func<Task>? GoogleSignInRequested;

    public SettingsPaneView()
    {
        InitializeComponent();
    }

    private void OnToggleIndexerClicked(object? sender, RoutedEventArgs e)
        => ToggleIndexerRequested?.Invoke();

    private void OnCompactClicked(object? sender, RoutedEventArgs e)
        => CompactRequested?.Invoke();

    private void OnIndexAllClicked(object? sender, RoutedEventArgs e)
        => IndexAllRequested?.Invoke();

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
        => RefreshRequested?.Invoke();

    private async void OnGoogleSignInClicked(object? sender, RoutedEventArgs e)
    {
        if (GoogleSignInRequested != null)
            await GoogleSignInRequested.Invoke();
    }
}
