using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quaero.Plugins.Abstractions;

namespace Quaero.Plugins.Json;

public class JsonSearchPlugin : ISearchPlugin
{
    private string _directory = string.Empty;
    private string _fileGlob = "**/*.json";
    private string? _titlePath;
    private string? _summaryPath;
    private string? _contentPath;
    private DateTime? _lastSuccessfulRun;

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.json",
        Name = "JSON Files",
        Description = "Indexes JSON files with configurable field mappings for title, summary, and content",
        Version = "1.0.0",
        SupportedFileExtensions = [".json"]
    };

    public IReadOnlyList<PluginSettingDescriptor> SettingDescriptors =>
    [
        new() { Key = "Directory", DisplayName = "Folder Path", Description = "Root folder to scan for JSON files", SettingType = PluginSettingType.FolderPath, IsRequired = true },
        new() { Key = "FileGlob", DisplayName = "File Pattern", Description = "Glob pattern for matching files (e.g. **/*.json)", SettingType = PluginSettingType.GlobPattern, DefaultValue = "**/*.json" },
        new() { Key = "TitlePath", DisplayName = "Title JSON Path", Description = "Dot-notation path to the title field (e.g. metadata.title). Leave blank for auto-detect.", SettingType = PluginSettingType.Text },
        new() { Key = "SummaryPath", DisplayName = "Summary JSON Path", Description = "Dot-notation path to the summary field (e.g. metadata.description)", SettingType = PluginSettingType.Text },
        new() { Key = "ContentPath", DisplayName = "Content JSON Path", Description = "Dot-notation path to the main content field (e.g. body.text)", SettingType = PluginSettingType.Text }
    ];

    public Task InitializeAsync(PluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration.Settings.TryGetValue("Directory", out var dir))
            _directory = dir;
        else if (configuration.Settings.TryGetValue("Directories", out var dirs))
            _directory = dirs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

        if (configuration.Settings.TryGetValue("FileGlob", out var glob) && !string.IsNullOrWhiteSpace(glob))
            _fileGlob = glob;

        _titlePath = configuration.Settings.GetValueOrDefault("TitlePath");
        _summaryPath = configuration.Settings.GetValueOrDefault("SummaryPath");
        _contentPath = configuration.Settings.GetValueOrDefault("ContentPath");
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
                doc = ParseJson(fullPath, content);
            }
            catch (Exception)
            {
                // Skip files that can't be read or parsed
            }

            if (doc != null)
                yield return doc;
        }
    }

    private DiscoveredDocument ParseJson(string filePath, string content)
    {
        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        // Use configured JSON paths if set, otherwise fall back to auto-detect
        var title = ResolveJsonPath(root, _titlePath)
                    ?? ExtractJsonField(root, "title", "name", "subject", "heading")
                    ?? Path.GetFileNameWithoutExtension(filePath);

        var summary = ResolveJsonPath(root, _summaryPath)
                      ?? ExtractJsonField(root, "summary", "description", "body", "content", "text")
                      ?? TruncateJson(content, 500);

        string plainText;
        var contentFromPath = ResolveJsonPath(root, _contentPath);
        if (contentFromPath != null)
            plainText = contentFromPath;
        else
            plainText = JsonToPlainText(root);

        return new DiscoveredDocument
        {
            Type = "json",
            Provider = "local-files",
            Location = Path.GetFullPath(filePath),
            Title = title,
            Summary = summary.Length > 500 ? summary[..500] + "..." : summary,
            Content = plainText,
            ContentHash = ComputeHash(content),
            ExtendedData = new Dictionary<string, string>
            {
                ["file_extension"] = ".json",
                ["file_size"] = new FileInfo(filePath).Length.ToString(),
                ["last_modified"] = File.GetLastWriteTimeUtc(filePath).ToString("O"),
                ["root_type"] = root.ValueKind.ToString()
            }
        };
    }

    /// <summary>
    /// Resolves a dot-notation JSON path (e.g. "metadata.title") against a JSON element.
    /// </summary>
    private static string? ResolveJsonPath(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(segment, out var next)) return null;
            current = next;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : current.GetRawText();
    }

    private static string? ExtractJsonField(JsonElement root, params string[] fieldNames)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        foreach (var name in fieldNames)
        {
            if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        // Case-insensitive fallback
        foreach (var name in fieldNames)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
        }

        return null;
    }

    private static string JsonToPlainText(JsonElement element)
    {
        var sb = new StringBuilder();
        ExtractStrings(element, sb);
        return sb.ToString();
    }

    private static void ExtractStrings(JsonElement element, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                sb.AppendLine(element.GetString());
                break;
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    sb.Append(prop.Name).Append(": ");
                    ExtractStrings(prop.Value, sb);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ExtractStrings(item, sb);
                break;
            case JsonValueKind.Number:
                sb.AppendLine(element.GetRawText());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                sb.AppendLine(element.GetRawText());
                break;
        }
    }

    private static string TruncateJson(string json, int maxLength)
    {
        return json.Length > maxLength ? json[..maxLength] + "..." : json;
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
