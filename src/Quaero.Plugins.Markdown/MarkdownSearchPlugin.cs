using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Quaero.Plugins.Abstractions;

namespace Quaero.Plugins.Markdown;

public class MarkdownSearchPlugin : ISearchPlugin
{
    private string _directory = string.Empty;
    private string _fileGlob = "**/*.md";
    private DateTime? _lastSuccessfulRun;
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.markdown",
        Name = "Markdown Files",
        Description = "Indexes Markdown (.md) files from a directory using glob patterns. Uses the first heading as the title.",
        Version = "1.0.0",
        SupportedFileExtensions = [".md", ".markdown"]
    };

    public IReadOnlyList<PluginSettingDescriptor> SettingDescriptors =>
    [
        new() { Key = "Directory", DisplayName = "Folder Path", Description = "Root folder to scan for Markdown files", SettingType = PluginSettingType.FolderPath, IsRequired = true },
        new() { Key = "FileGlob", DisplayName = "File Pattern", Description = "Glob pattern for matching files (e.g. **/*.md)", SettingType = PluginSettingType.GlobPattern, DefaultValue = "**/*.md" }
    ];

    public Task InitializeAsync(PluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration.Settings.TryGetValue("Directory", out var dir))
            _directory = dir;
        // Backwards compat: support old semicolon-separated Directories setting
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
                doc = ParseMarkdown(fullPath, content);
            }
            catch (Exception)
            {
                // Skip files that can't be read
            }

            if (doc != null)
                yield return doc;
        }
    }

    private static DiscoveredDocument ParseMarkdown(string filePath, string content)
    {
        var document = Markdig.Markdown.Parse(content, Pipeline);
        var title = ExtractTitle(document) ?? Path.GetFileNameWithoutExtension(filePath);
        var summary = ExtractSummary(document);
        var plainText = Markdig.Markdown.ToPlainText(content, Pipeline);

        return new DiscoveredDocument
        {
            Type = "markdown",
            Provider = "local-files",
            Location = Path.GetFullPath(filePath),
            Title = title,
            Summary = summary,
            Content = plainText,
            ContentHash = ComputeHash(content),
            ExtendedData = new Dictionary<string, string>
            {
                ["file_extension"] = Path.GetExtension(filePath),
                ["file_size"] = new FileInfo(filePath).Length.ToString(),
                ["last_modified"] = File.GetLastWriteTimeUtc(filePath).ToString("O")
            }
        };
    }

    private static string? ExtractTitle(MarkdownDocument document)
    {
        var heading = document.Descendants<HeadingBlock>().FirstOrDefault(h => h.Level == 1);
        if (heading?.Inline == null) return null;

        var sb = new StringBuilder();
        foreach (var inline in heading.Inline)
        {
            if (inline is LiteralInline literal)
                sb.Append(literal.Content);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string ExtractSummary(MarkdownDocument document)
    {
        var paragraph = document.Descendants<ParagraphBlock>().FirstOrDefault();
        if (paragraph?.Inline == null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var inline in paragraph.Inline)
        {
            if (inline is LiteralInline literal)
                sb.Append(literal.Content);
        }

        var text = sb.ToString();
        return text.Length > 500 ? text[..500] + "..." : text;
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
