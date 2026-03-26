using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Quaero.Core.Models;

namespace Quaero.UI.ViewModels;

/// <summary>
/// ViewModel for the Settings tab. Manages the indexer process lifecycle
/// and exposes configuration paths.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IndexConfiguration _config;
    private Process? _indexerProcess;
    private bool _isIndexerRunning;
    private string _indexerStatusText = "Stopped";
    private string _indexerOutput = string.Empty;

    public SettingsViewModel(IndexConfiguration config)
    {
        _config = config;
    }

    public bool IsIndexerRunning
    {
        get => _isIndexerRunning;
        private set
        {
            if (SetField(ref _isIndexerRunning, value))
            {
                OnPropertyChanged(nameof(IsIndexerStopped));
                OnPropertyChanged(nameof(StartStopButtonText));
            }
        }
    }

    public bool IsIndexerStopped => !_isIndexerRunning;
    public string StartStopButtonText => _isIndexerRunning ? "Stop Indexer" : "Start Indexer";

    public string IndexerStatusText
    {
        get => _indexerStatusText;
        private set => SetField(ref _indexerStatusText, value);
    }

    public string IndexerOutput
    {
        get => _indexerOutput;
        private set => SetField(ref _indexerOutput, value);
    }

    public string DatabasePath => _config.DatabasePath;
    public string PluginsDirectory => _config.PluginsDirectory;
    public string DataSourcesConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Quaero", "datasources.json");

    public string IndexerExePath
    {
        get
        {
            // The indexer is bundled alongside the UI in the indexer/ subfolder
            var bundled = Path.Combine(AppContext.BaseDirectory, "indexer", "Quaero.Indexer.exe");
            if (File.Exists(bundled)) return bundled;

            // Fallback: check for .dll (cross-platform)
            var bundledDll = Path.Combine(AppContext.BaseDirectory, "indexer", "Quaero.Indexer.dll");
            if (File.Exists(bundledDll)) return bundledDll;

            // Dev fallback: sibling project output
            var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Quaero.Indexer", "bin", "Debug", "net10.0", "Quaero.Indexer.exe");
            if (File.Exists(devPath)) return Path.GetFullPath(devPath);

            return "Quaero.Indexer";
        }
    }

    public void ToggleIndexer()
    {
        if (IsIndexerRunning)
            StopIndexer();
        else
            StartIndexer();
    }

    public void StartIndexer()
    {
        if (IsIndexerRunning) return;

        try
        {
            var exePath = IndexerExePath;
            var isDll = exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

            var psi = new ProcessStartInfo
            {
                FileName = isDll ? "dotnet" : exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (isDll)
                psi.ArgumentList.Add(exePath);

            // Pass the plugins directory so the indexer uses the same one
            psi.Environment["QUAERO_PLUGINS_DIR"] = _config.PluginsDirectory;

            _indexerProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _indexerProcess.OutputDataReceived += OnOutputReceived;
            _indexerProcess.ErrorDataReceived += OnOutputReceived;
            _indexerProcess.Exited += OnIndexerExited;

            _indexerProcess.Start();
            _indexerProcess.BeginOutputReadLine();
            _indexerProcess.BeginErrorReadLine();

            IsIndexerRunning = true;
            IndexerStatusText = $"Running (PID {_indexerProcess.Id})";
            AppendOutput($"[Indexer started: {exePath}, PID {_indexerProcess.Id}]");
        }
        catch (Exception ex)
        {
            IndexerStatusText = $"Failed to start: {ex.Message}";
            AppendOutput($"[Error starting indexer: {ex.Message}]");
        }
    }

    public void StopIndexer()
    {
        if (_indexerProcess == null || !IsIndexerRunning) return;

        try
        {
            _indexerProcess.Kill(entireProcessTree: true);
            AppendOutput("[Indexer stop requested]");
        }
        catch (Exception ex)
        {
            AppendOutput($"[Error stopping indexer: {ex.Message}]");
        }
    }

    /// <summary>
    /// Clean up process on app shutdown.
    /// </summary>
    public void Shutdown()
    {
        if (_indexerProcess != null && !_indexerProcess.HasExited)
        {
            try { _indexerProcess.Kill(entireProcessTree: true); } catch { }
        }
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
            AppendOutput(e.Data);
    }

    private void OnIndexerExited(object? sender, EventArgs e)
    {
        var exitCode = _indexerProcess?.ExitCode;
        IsIndexerRunning = false;
        IndexerStatusText = $"Stopped (exit code {exitCode})";
        AppendOutput($"[Indexer exited with code {exitCode}]");
    }

    private void AppendOutput(string line)
    {
        // Keep last 200 lines
        var lines = IndexerOutput.Split('\n');
        if (lines.Length > 200)
            IndexerOutput = string.Join('\n', lines[^200..]) + "\n" + line;
        else
            IndexerOutput = IndexerOutput + (IndexerOutput.Length > 0 ? "\n" : "") + line;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
