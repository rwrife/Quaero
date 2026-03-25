using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using Quaero.Plugins.Abstractions;

namespace Quaero.Plugins.Imap;

public class ImapSearchPlugin : ISearchPlugin
{
    private string _host = string.Empty;
    private int _port = 993;
    private bool _useSsl = true;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _provider = "email";
    private int _maxMessages = 500;

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.imap",
        Name = "IMAP Email",
        Description = "Indexes emails from IMAP mail servers (Gmail, Outlook, etc.)",
        Version = "1.0.0",
        SupportedFileExtensions = []
    };

    public Task InitializeAsync(PluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var settings = configuration.Settings;
        _host = settings.GetValueOrDefault("Host", "imap.gmail.com");
        _port = int.TryParse(settings.GetValueOrDefault("Port", "993"), out var p) ? p : 993;
        _useSsl = !string.Equals(settings.GetValueOrDefault("UseSsl", "true"), "false", StringComparison.OrdinalIgnoreCase);
        _username = settings.GetValueOrDefault("Username", string.Empty);
        _password = settings.GetValueOrDefault("Password", string.Empty);
        _provider = settings.GetValueOrDefault("Provider", "email");
        _maxMessages = int.TryParse(settings.GetValueOrDefault("MaxMessages", "500"), out var m) ? m : 500;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DiscoveredDocument> DiscoverDocumentsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password))
            yield break;

        using var client = new ImapClient();
        await client.ConnectAsync(_host, _port, _useSsl, cancellationToken);
        await client.AuthenticateAsync(_username, _password, cancellationToken);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uids = await inbox.SearchAsync(SearchQuery.All, cancellationToken);
        var toProcess = uids.Reverse().Take(_maxMessages);

        foreach (var uid in toProcess)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            MimeMessage? message = null;
            try
            {
                message = await inbox.GetMessageAsync(uid, cancellationToken);
            }
            catch (Exception)
            {
                continue;
            }

            if (message == null) continue;

            var body = message.TextBody ?? message.HtmlBody ?? string.Empty;
            // Strip HTML tags for indexing if we only have HTML
            if (message.TextBody == null && message.HtmlBody != null)
                body = StripHtml(body);

            var emailLink = BuildEmailLink(uid, _host);

            yield return new DiscoveredDocument
            {
                Type = "email",
                Provider = _provider,
                Location = emailLink,
                Title = message.Subject ?? "(No Subject)",
                Summary = body.Length > 500 ? body[..500] + "..." : body,
                Content = body,
                ContentHash = ComputeHash($"{message.MessageId}:{message.Date:O}:{message.Subject}"),
                ExtendedData = new Dictionary<string, string>
                {
                    ["from"] = message.From?.ToString() ?? string.Empty,
                    ["to"] = message.To?.ToString() ?? string.Empty,
                    ["date"] = message.Date.ToString("O"),
                    ["message_id"] = message.MessageId ?? string.Empty,
                    ["has_attachments"] = message.Attachments.Any().ToString()
                }
            };
        }

        await client.DisconnectAsync(true, cancellationToken);
    }

    private static string BuildEmailLink(UniqueId uid, string host)
    {
        // For Gmail, construct a web link; otherwise use a generic reference
        if (host.Contains("gmail", StringComparison.OrdinalIgnoreCase))
            return $"https://mail.google.com/mail/u/0/#inbox/{uid}";
        return $"imap://{host}/INBOX;UID={uid}";
    }

    private static string StripHtml(string html)
    {
        // Simple HTML tag removal for indexing purposes
        var result = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
        return result.Trim();
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
