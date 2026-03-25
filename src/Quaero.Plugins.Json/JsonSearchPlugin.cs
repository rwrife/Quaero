using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quaero.Plugins.Abstractions;

namespace Quaero.Plugins.Json;

public class JsonSearchPlugin : ISearchPlugin
{
    private readonly List<string> _directories = new();

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.json",
        Name = "JSON Files",
        Description = "Indexes JSON (.json) files from configured directories",
        Version = "1.0.0",
        SupportedFileExtensions = [".json"]
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

            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                DiscoveredDocument? doc = null;
                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    doc = ParseJson(file, content);
                }
                catch (Exception)
                {
                    // Skip files that can't be read or parsed
                }

                if (doc != null)
                    yield return doc;
            }
        }
    }

    private static DiscoveredDocument ParseJson(string filePath, string content)
    {
        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        var title = ExtractJsonField(root, "title", "name", "subject", "heading")
                    ?? Path.GetFileNameWithoutExtension(filePath);

        var summary = ExtractJsonField(root, "summary", "description", "body", "content", "text")
                      ?? TruncateJson(content, 500);

        var plainText = JsonToPlainText(root);

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
    /// Attempts to find a meaningful field from the JSON by checking common property names.
    /// </summary>
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

    /// <summary>
    /// Recursively extracts all string values from JSON into searchable plain text.
    /// </summary>
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
