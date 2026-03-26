using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Quaero.UI.Views.Panes;

public partial class ChatHistoryPaneView : UserControl
{
    public event Action? ReusePromptRequested;

    public ChatHistoryPaneView()
    {
        InitializeComponent();
    }

    private void OnReuseChatPromptClicked(object? sender, RoutedEventArgs e)
        => ReusePromptRequested?.Invoke();
}
