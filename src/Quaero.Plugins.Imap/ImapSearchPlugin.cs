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
    private DateTime? _lastSuccessfulRun;

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.imap",
        Name = "IMAP Email",
        Description = "Indexes emails from generic IMAP mail servers",
        Version = "1.0.0",
        SupportedFileExtensions = []
    };

    public IReadOnlyList<PluginSettingDescriptor> SettingDescriptors =>
    [
        new() { Key = "Host", DisplayName = "IMAP Server", Description = "IMAP server hostname", SettingType = PluginSettingType.Text, DefaultValue = "imap.example.com", IsRequired = true },
        new() { Key = "Port", DisplayName = "Port", Description = "IMAP server port", SettingType = PluginSettingType.Number, DefaultValue = "993" },
        new() { Key = "UseSsl", DisplayName = "Use SSL", Description = "Connect using SSL/TLS", SettingType = PluginSettingType.Boolean, DefaultValue = "true" },
        new() { Key = "Username", DisplayName = "Username", Description = "Email address or username", SettingType = PluginSettingType.Text, IsRequired = true },
        new() { Key = "Password", DisplayName = "Password", Description = "Password or app-specific password", SettingType = PluginSettingType.Password, IsRequired = true },
        new() { Key = "Provider", DisplayName = "Provider Name", Description = "Display name for this email source", SettingType = PluginSettingType.Text, DefaultValue = "imap" },
        new() { Key = "MaxMessages", DisplayName = "Max Messages", Description = "Maximum number of messages to index", SettingType = PluginSettingType.Number, DefaultValue = "500" }
    ];

    public Task InitializeAsync(PluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var settings = configuration.Settings;
        _host = settings.GetValueOrDefault("Host", "imap.gmail.com");
        _port = int.TryParse(settings.GetValueOrDefault("Port", "993"), out var p) ? p : 993;
        _useSsl = !string.Equals(settings.GetValueOrDefault("UseSsl", "true"), "false", StringComparison.OrdinalIgnoreCase);
        _username = settings.GetValueOrDefault("Username", string.Empty);
        _password = settings.GetValueOrDefault("Password", string.Empty);
        _provider = settings.GetValueOrDefault("Provider", "imap");
        _maxMessages = int.TryParse(settings.GetValueOrDefault("MaxMessages", "500"), out var m) ? m : 500;
        _lastSuccessfulRun = configuration.LastSuccessfulRun;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DiscoveredDocument> DiscoverDocumentsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_username) || string.IsNullOrWhiteSpace(_password))
            yield break;

        using var client = new ImapClient();
        await client.ConnectAsync(_host, _port, _useSsl, cancellationToken);
        await client.AuthenticateAsync(_username, _password, cancellationToken);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        // Incremental: only fetch emails since last successful run
        MailKit.Search.SearchQuery searchQuery;
        if (_lastSuccessfulRun.HasValue)
        {
            searchQuery = MailKit.Search.SearchQuery.DeliveredAfter(_lastSuccessfulRun.Value);
        }
        else
        {
            searchQuery = MailKit.Search.SearchQuery.All;
        }

        var uids = await inbox.SearchAsync(searchQuery, cancellationToken);
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
