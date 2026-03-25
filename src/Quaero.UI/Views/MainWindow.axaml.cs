using Avalonia.Controls;
using Avalonia.Interactivity;
using Quaero.UI.ViewModels;

namespace Quaero.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    private async void OnIndexAllClicked(object? sender, RoutedEventArgs e)
        => await VM.RunIndexingAsync();

    private void OnCompactClicked(object? sender, RoutedEventArgs e)
        => VM.CompactIndex();

    private async void OnAddDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var plugins = VM.DataSourcesVM.AvailablePluginTypes;
        if (plugins.Count == 0)
        {
            VM.StatusText = "No plugins found in the plugins folder. Place plugin DLLs there first.";
            return;
        }

        var editVM = new EditDataSourceViewModel(plugins);
        var dialog = new EditDataSourceDialog { DataContext = editVM };
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            VM.DataSourcesVM.AddDataSource(editVM.ToDataSource());
            VM.StatusText = $"Added data source: {editVM.Name}";
        }
    }

    private async void OnEditDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;

        var plugins = VM.DataSourcesVM.AvailablePluginTypes;
        var editVM = new EditDataSourceViewModel(plugins, selected.Model);
        var dialog = new EditDataSourceDialog { DataContext = editVM };
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            VM.DataSourcesVM.UpdateDataSource(editVM.ToDataSource());
            VM.StatusText = $"Updated data source: {editVM.Name}";
        }
    }

    private void OnRemoveDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        VM.DataSourcesVM.RemoveDataSource(selected.Id);
        VM.StatusText = $"Removed data source: {selected.Name}";
    }

    private void OnToggleDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        VM.DataSourcesVM.ToggleDataSource(selected.Id);
    }

    private async void OnRunSelectedClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        await VM.DataSourcesVM.RunSingleDataSourceAsync(selected.Id);
    }

    private async void OnRunAllClicked(object? sender, RoutedEventArgs e)
        => await VM.DataSourcesVM.RunIndexingAsync();

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
        => VM.DataSourcesVM.RefreshDataSources();
}
