using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Quaero.Plugins.Abstractions;

namespace Quaero.Plugins.Text;

public class TextSearchPlugin : ISearchPlugin
{
    private string _directory = string.Empty;
    private string _fileGlob = "**/*.txt";
    private DateTime? _lastSuccessfulRun;

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.text",
        Name = "Text Files",
        Description = "Indexes plain text files from a directory using glob patterns",
        Version = "1.0.0",
        SupportedFileExtensions = [".txt"]
    };

    public IReadOnlyList<PluginSettingDescriptor> SettingDescriptors =>
    [
        new() { Key = "Directory", DisplayName = "Folder Path", Description = "Root folder to scan for text files", SettingType = PluginSettingType.FolderPath, IsRequired = true },
        new() { Key = "FileGlob", DisplayName = "File Pattern", Description = "Glob pattern for matching files (e.g. **/*.txt, **/*.log)", SettingType = PluginSettingType.GlobPattern, DefaultValue = "**/*.txt" }
    ];

    public Task InitializeAsync(PluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration.Settings.TryGetValue("Directory", out var dir))
            _directory = dir;
        else if (configuration.Settings.TryGetValue("Directories", out var dirs))
            _directory = dirs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

        if (configuration.Settings.TryGetValue("FileGlob", out var glob) && !string.IsNullOrWhiteSpace(glob))
            _fileGlob = glob;

        _lastSuccessfulRun = configuration.LastSuccessfulRun;

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DiscoveredDocument> DiscoverDocumentsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_directory) || !Directory.Exists(_directory))
            yield break;

        var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher();
        matcher.AddInclude(_fileGlob);
        var result = matcher.Execute(new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(new DirectoryInfo(_directory)));

        foreach (var file in result.Files)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            var fullPath = Path.Combine(_directory, file.Path);

            // Incremental: skip files not modified since last successful run
            if (_lastSuccessfulRun.HasValue)
            {
                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(fullPath);
                    if (lastWrite < _lastSuccessfulRun.Value) continue;
                }
                catch { continue; }
            }

            DiscoveredDocument? doc = null;
            try
            {
                var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                doc = new DiscoveredDocument
                {
                    Type = "text",
                    Provider = "local-files",
                    Location = Path.GetFullPath(fullPath),
                    Title = Path.GetFileNameWithoutExtension(fullPath),
                    Summary = content.Length > 500 ? content[..500] + "..." : content,
                    Content = content,
                    ContentHash = ComputeHash(content),
                    ExtendedData = new Dictionary<string, string>
                    {
                        ["file_extension"] = Path.GetExtension(fullPath),
                        ["file_size"] = new FileInfo(fullPath).Length.ToString(),
                        ["last_modified"] = File.GetLastWriteTimeUtc(fullPath).ToString("O")
                    }
                };
            }
            catch (Exception)
            {
                // Skip files that can't be read
            }

            if (doc != null)
                yield return doc;
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
