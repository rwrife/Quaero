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
    private readonly List<string> _directories = new();
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.markdown",
        Name = "Markdown Files",
        Description = "Indexes Markdown (.md) files from configured directories",
        Version = "1.0.0",
        SupportedFileExtensions = [".md", ".markdown"]
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

            var files = Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*.markdown", SearchOption.AllDirectories));

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                DiscoveredDocument? doc = null;
                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    doc = ParseMarkdown(file, content);
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
