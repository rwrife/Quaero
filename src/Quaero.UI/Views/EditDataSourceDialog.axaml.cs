using Avalonia.Controls;
using Avalonia.Interactivity;
using Quaero.UI.ViewModels;

namespace Quaero.UI.Views;

public partial class EditDataSourceDialog : Window
{
    public EditDataSourceDialog()
    {
        InitializeComponent();
    }

    private EditDataSourceViewModel VM => (EditDataSourceViewModel)DataContext!;

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (!VM.Validate(out var error))
        {
            // Simple inline validation feedback via title
            Title = $"Error: {error}";
            return;
        }
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
