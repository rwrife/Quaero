using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Quaero.Plugins.Abstractions;

namespace Quaero.Plugins.Text;

public class TextSearchPlugin : ISearchPlugin
{
    private readonly List<string> _directories = new();

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.text",
        Name = "Text Files",
        Description = "Indexes plain text (.txt) files from configured directories",
        Version = "1.0.0",
        SupportedFileExtensions = [".txt"]
    };

    public Task InitializeAsync(PluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration.Settings.TryGetValue("Directories", out var dirs))
        {
            _directories.AddRange(dirs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DiscoveredDocument> DiscoverDocumentsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var directory in _directories)
        {
            if (!Directory.Exists(directory)) continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.txt", SearchOption.AllDirectories))
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                DiscoveredDocument? doc = null;
                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    doc = new DiscoveredDocument
                    {
                        Type = "text",
                        Provider = "local-files",
                        Location = Path.GetFullPath(file),
                        Title = Path.GetFileNameWithoutExtension(file),
                        Summary = content.Length > 500 ? content[..500] + "..." : content,
                        Content = content,
                        ContentHash = ComputeHash(content),
                        ExtendedData = new Dictionary<string, string>
                        {
                            ["file_extension"] = ".txt",
                            ["file_size"] = new FileInfo(file).Length.ToString(),
                            ["last_modified"] = File.GetLastWriteTimeUtc(file).ToString("O")
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
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
