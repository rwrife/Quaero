using Avalonia.Controls;
using Avalonia.Interactivity;
using Quaero.UI.ViewModels;

namespace Quaero.UI.Views;

public partial class EditDataSourceDialog : Window
{
    public EditDataSourceDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
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

    private void OnOpened(object? sender, EventArgs e)
    {
        if (Owner is MainWindow owner
            && owner.DataContext is MainWindowViewModel mainVm)
        {
            var secretBox = this.FindControl<TextBox>("GmailClientSecretBox");
            GmailClientIdBox.Text = mainVm.SettingsVM.GoogleClientId;
            if (secretBox != null)
                secretBox.Text = mainVm.SettingsVM.GoogleClientSecret;
            GmailSignInStatusText.Text = mainVm.SettingsVM.GoogleSignInStatus;
        }
    }

    private async void OnGoogleSignInClicked(object? sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow owner
            && owner.DataContext is MainWindowViewModel mainVm)
        {
            var secretBox = this.FindControl<TextBox>("GmailClientSecretBox");
            mainVm.SettingsVM.GoogleClientId = GmailClientIdBox.Text?.Trim() ?? string.Empty;
            mainVm.SettingsVM.GoogleClientSecret = secretBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(mainVm.SettingsVM.GoogleClientId))
            {
                Title = "Google Sign-in: set OAuth Client ID";
                GmailSignInStatusText.Text = "Enter OAuth Client ID.";
                return;
            }

            await mainVm.SettingsVM.SignInWithGoogleAsync();
            Title = mainVm.SettingsVM.GoogleSignInStatus;
            GmailSignInStatusText.Text = mainVm.SettingsVM.GoogleSignInStatus;
            return;
        }

        Title = "Google Sign-in unavailable: could not access main window context";
        GmailSignInStatusText.Text = Title;
    }
}
