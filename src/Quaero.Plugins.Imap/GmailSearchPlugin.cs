using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quaero.Plugins.Abstractions;

namespace Quaero.Plugins.Imap;

public class GmailSearchPlugin : ISearchPlugin
{
    private const string GmailApiBaseUrl = "https://gmail.googleapis.com/gmail/v1/users/me";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string _username = string.Empty;
    private string _provider = "gmail";
    private int _maxMessages = 500;
    private DateTime? _lastSuccessfulRun;

    public PluginMetadata Metadata => new()
    {
        Id = "quaero.plugins.gmail",
        Name = "Gmail",
        Description = "Indexes Gmail email using Google OAuth",
        Version = "1.0.0",
        SupportedFileExtensions = []
    };

    public IReadOnlyList<PluginSettingDescriptor> SettingDescriptors =>
    [
        new() { Key = "Username", DisplayName = "Gmail Address", Description = "Optional. Used for display only; Gmail API uses the signed-in account.", SettingType = PluginSettingType.Text },
        new() { Key = "Provider", DisplayName = "Provider Name", Description = "Display name for this email source", SettingType = PluginSettingType.Text, DefaultValue = "gmail" },
        new() { Key = "MaxMessages", DisplayName = "Max Messages", Description = "Maximum number of messages to index", SettingType = PluginSettingType.Number, DefaultValue = "500" }
    ];

    public Task InitializeAsync(PluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var settings = configuration.Settings;
        _username = settings.GetValueOrDefault("Username", string.Empty);
        _provider = settings.GetValueOrDefault("Provider", "gmail");
        _maxMessages = int.TryParse(settings.GetValueOrDefault("MaxMessages", "500"), out var m) ? m : 500;
        _lastSuccessfulRun = configuration.LastSuccessfulRun;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DiscoveredDocument> DiscoverDocumentsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LogDebug("Starting Gmail discovery.");
        var oauth = await TryGetGoogleAccessTokenAsync(cancellationToken);
        if (oauth == null)
            throw new InvalidOperationException("Google OAuth token unavailable. Sign in to Gmail in Settings and try again.");
        LogDebug($"OAuth token acquired. Email='{oauth.Email}'.");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oauth.AccessToken);

        var sourceKey = string.IsNullOrWhiteSpace(_username)
            ? "gmail"
            : $"gmail:{_username.Trim().ToLowerInvariant()}";
        var cursor = await LoadCursorAsync(sourceKey, cancellationToken);
        Queue<string> retainedIds;
        if (cursor?.LastInternalDateMs is long newestIndexedMs)
        {
            var newestQuery = BuildQueryAfterNewestIndexedDate(newestIndexedMs);
            LogDebug($"Cursor found. Looking for emails newer than newest indexed date with query '{newestQuery}'.");
            retainedIds = await CollectRetainedMessageIdsAsync(http, newestQuery, cancellationToken);

            if (retainedIds.Count == 0 && !string.IsNullOrWhiteSpace(cursor.LastMessageId))
            {
                LogDebug($"No results from date query. Falling back to cursor walk from message id '{cursor.LastMessageId}'.");
                retainedIds = await CollectRetainedMessageIdsByWalkingFromCursorAsync(http, cursor.LastMessageId, cancellationToken);
            }
        }
        else if (!string.IsNullOrWhiteSpace(cursor?.LastMessageId))
        {
            LogDebug($"Cursor found. Walking mailbox from last message id '{cursor.LastMessageId}' for next block.");
            retainedIds = await CollectRetainedMessageIdsByWalkingFromCursorAsync(http, cursor.LastMessageId, cancellationToken);
        }
        else
        {
            LogDebug("No cursor found. Building first block from oldest emails.");
            retainedIds = await CollectRetainedMessageIdsAsync(http, null, cancellationToken);
        }

        if (retainedIds.Count == 0)
        {
            LogDebug("No messages found for this run.");
            yield break;
        }

        long? lastProcessedInternalDateMs = cursor?.LastInternalDateMs;
        string? lastProcessedMessageId = cursor?.LastMessageId;

        foreach (var messageId in retainedIds.Reverse())
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            using var messageResponse = await http.GetAsync(
                $"{GmailApiBaseUrl}/messages/{Uri.EscapeDataString(messageId)}?format=full",
                cancellationToken);
            var messageJson = await messageResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!messageResponse.IsSuccessStatusCode)
                continue;

            GmailMessageResponse? message;
            try
            {
                message = JsonSerializer.Deserialize<GmailMessageResponse>(messageJson, JsonOptions);
            }
            catch
            {
                continue;
            }

            if (message?.Id == null)
                continue;

            var headers = ToHeaderDictionary(message.Payload?.Headers);
            var subject = headers.GetValueOrDefault("subject") ?? "(No Subject)";
            var from = headers.GetValueOrDefault("from") ?? string.Empty;
            var to = headers.GetValueOrDefault("to") ?? string.Empty;
            var dateHeader = headers.GetValueOrDefault("date") ?? string.Empty;

            var body = ExtractBodyText(message.Payload);
            if (string.IsNullOrWhiteSpace(body))
                body = message.Snippet ?? string.Empty;

            var contentDate = ParseInternalDate(message.InternalDate);
            var provider = _provider;
            if (string.IsNullOrWhiteSpace(provider))
                provider = "gmail";

            var indexedDateMs = ParseIndexedDateMilliseconds(dateHeader, message.InternalDate);
            if (indexedDateMs.HasValue && (!lastProcessedInternalDateMs.HasValue || indexedDateMs.Value > lastProcessedInternalDateMs.Value))
                lastProcessedInternalDateMs = indexedDateMs.Value;
            lastProcessedMessageId = message.Id;

            yield return new DiscoveredDocument
            {
                Type = "email",
                Provider = provider,
                Location = $"https://mail.google.com/mail/u/0/#inbox/{message.Id}",
                Title = subject,
                Summary = body.Length > 500 ? body[..500] + "..." : body,
                Content = body,
                ContentHash = ComputeHash($"{message.Id}:{message.InternalDate}:{subject}"),
                ExtendedData = new Dictionary<string, string>
                {
                    ["from"] = from,
                    ["to"] = to,
                    ["date"] = contentDate?.ToString("O") ?? dateHeader,
                    ["message_id"] = headers.GetValueOrDefault("message-id") ?? message.Id,
                    ["has_attachments"] = HasAttachments(message.Payload).ToString()
                }
            };
        }

        if (!string.IsNullOrWhiteSpace(lastProcessedMessageId))
        {
            await SaveCursorAsync(
                sourceKey,
                new GmailCursor
                {
                    LastMessageId = lastProcessedMessageId,
                    LastInternalDateMs = lastProcessedInternalDateMs
                },
                cancellationToken);
            LogDebug($"Updated cursor for '{sourceKey}' to message '{lastProcessedMessageId}' (ms={lastProcessedInternalDateMs}).");
        }
    }

    private async Task<GoogleOAuthAccessToken?> TryGetGoogleAccessTokenAsync(CancellationToken cancellationToken)
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero",
            "google-oauth.json");
        if (!File.Exists(settingsPath))
        {
            LogDebug($"OAuth settings file not found at '{settingsPath}'.");
            return null;
        }

        GoogleOAuthSettingsDocument? settings;
        try
        {
            var json = await File.ReadAllTextAsync(settingsPath, cancellationToken);
            settings = JsonSerializer.Deserialize<GoogleOAuthSettingsDocument>(json, JsonOptions);
        }
        catch
        {
            LogDebug("Failed to read/parse OAuth settings file.");
            return null;
        }

        if (settings == null
            || string.IsNullOrWhiteSpace(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.RefreshToken))
        {
            LogDebug("OAuth settings are missing required client_id or refresh_token.");
            return null;
        }

        using var http = new HttpClient();
        var payload = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["refresh_token"] = settings.RefreshToken,
            ["grant_type"] = "refresh_token"
        };

        if (!string.IsNullOrWhiteSpace(settings.ClientSecret))
            payload["client_secret"] = settings.ClientSecret;

        using var response = await http.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(payload),
            cancellationToken);
        LogDebug($"Token refresh response status: {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            var tokenError = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Google token refresh failed ({(int)response.StatusCode}): {tokenError}");
        }

        var tokenJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var tokenProp)
            ? tokenProp.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        return new GoogleOAuthAccessToken
        {
            Email = settings.Email ?? string.Empty,
            AccessToken = accessToken
        };
    }

    private static string StripHtml(string html)
    {
        var result = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
        return result.Trim();
    }

    private static void LogDebug(string message)
        => Console.WriteLine($"[GmailSearchPlugin] {message}");

    private static string BuildQueryAfterNewestIndexedDate(long newestIndexedDateMs)
    {
        var seconds = Math.Max(0, newestIndexedDateMs / 1000);
        return $"after:{seconds}";
    }

    private async Task<Queue<string>> CollectRetainedMessageIdsAsync(HttpClient http, string? query, CancellationToken cancellationToken)
    {
        var retainedIds = new Queue<string>();
        string? pageToken = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var listUrl = $"{GmailApiBaseUrl}/messages?maxResults=500";
            if (!string.IsNullOrWhiteSpace(query))
                listUrl += $"&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrWhiteSpace(pageToken))
                listUrl += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var listResponse = await http.GetAsync(listUrl, cancellationToken);
            var listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken);
            LogDebug($"List response status: {(int)listResponse.StatusCode} (query='{query ?? "<none>"}', pageToken='{pageToken ?? "<none>"}')");
            if (!listResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gmail list request failed ({(int)listResponse.StatusCode}): {listJson}");

            GmailListMessagesResponse? list;
            try
            {
                list = JsonSerializer.Deserialize<GmailListMessagesResponse>(listJson, JsonOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse Gmail list response: {ex.Message}");
            }

            var messages = list?.Messages ?? [];
            if (messages.Count == 0)
                break;

            foreach (var item in messages)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                    continue;

                retainedIds.Enqueue(item.Id);
                while (retainedIds.Count > _maxMessages)
                    retainedIds.Dequeue();
            }

            pageToken = list?.NextPageToken;
            if (string.IsNullOrWhiteSpace(pageToken))
                break;
        }

        LogDebug($"Retained {retainedIds.Count} message id(s) for processing.");
        return retainedIds;
    }

    private async Task<Queue<string>> CollectRetainedMessageIdsByWalkingFromCursorAsync(HttpClient http, string lastMessageId, CancellationToken cancellationToken)
    {
        var retainedIds = new Queue<string>();
        string? pageToken = null;
        var foundCursor = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var listUrl = $"{GmailApiBaseUrl}/messages?maxResults=500";
            if (!string.IsNullOrWhiteSpace(pageToken))
                listUrl += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var listResponse = await http.GetAsync(listUrl, cancellationToken);
            var listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!listResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gmail list request failed ({(int)listResponse.StatusCode}): {listJson}");

            GmailListMessagesResponse? list;
            try
            {
                list = JsonSerializer.Deserialize<GmailListMessagesResponse>(listJson, JsonOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse Gmail list response: {ex.Message}");
            }

            var messages = list?.Messages ?? [];
            if (messages.Count == 0)
                break;

            foreach (var item in messages)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                    continue;

                if (string.Equals(item.Id, lastMessageId, StringComparison.Ordinal))
                {
                    foundCursor = true;
                    break;
                }

                retainedIds.Enqueue(item.Id);
                while (retainedIds.Count > _maxMessages)
                    retainedIds.Dequeue();
            }

            if (foundCursor)
                break;

            pageToken = list?.NextPageToken;
            if (string.IsNullOrWhiteSpace(pageToken))
                break;
        }

        LogDebug(foundCursor
            ? $"Cursor walk found cursor and retained {retainedIds.Count} candidate id(s)."
            : "Cursor walk did not find the stored cursor message id.");
        return foundCursor ? retainedIds : new Queue<string>();
    }

    private static long? ParseInternalDateMilliseconds(string? internalDate)
    {
        if (!long.TryParse(internalDate, out var ms))
            return null;

        return ms;
    }

    private static long? ParseIndexedDateMilliseconds(string? headerDate, string? internalDate)
    {
        if (!string.IsNullOrWhiteSpace(headerDate)
            && DateTimeOffset.TryParse(headerDate, out var parsedHeaderDate))
        {
            return parsedHeaderDate.ToUniversalTime().ToUnixTimeMilliseconds();
        }

        return ParseInternalDateMilliseconds(internalDate);
    }

    private static string GetCursorPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero",
            "gmail-cursors.json");
    }

    private static async Task<GmailCursor?> LoadCursorAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var path = GetCursorPath();
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var map = JsonSerializer.Deserialize<Dictionary<string, GmailCursor>>(json, JsonOptions);
            if (map == null)
                return null;

            return map.TryGetValue(sourceKey, out var cursor) ? cursor : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveCursorAsync(string sourceKey, GmailCursor cursor, CancellationToken cancellationToken)
    {
        var path = GetCursorPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Dictionary<string, GmailCursor> map;
        if (File.Exists(path))
        {
            try
            {
                var existing = await File.ReadAllTextAsync(path, cancellationToken);
                map = JsonSerializer.Deserialize<Dictionary<string, GmailCursor>>(existing, JsonOptions) ?? new();
            }
            catch
            {
                map = new();
            }
        }
        else
        {
            map = new();
        }

        map[sourceKey] = cursor;
        var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static Dictionary<string, string> ToHeaderDictionary(List<GmailHeader>? headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers == null)
            return map;

        foreach (var header in headers)
        {
            if (!string.IsNullOrWhiteSpace(header.Name) && !map.ContainsKey(header.Name))
                map[header.Name] = header.Value ?? string.Empty;
        }

        return map;
    }

    private static string ExtractBodyText(GmailMessagePart? payload)
    {
        if (payload == null)
            return string.Empty;

        var plain = ExtractMimeBody(payload, "text/plain");
        if (!string.IsNullOrWhiteSpace(plain))
            return plain;

        var html = ExtractMimeBody(payload, "text/html");
        return string.IsNullOrWhiteSpace(html) ? string.Empty : StripHtml(html);
    }

    private static string ExtractMimeBody(GmailMessagePart part, string mimeType)
    {
        if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
        {
            var data = part.Body?.Data;
            if (!string.IsNullOrWhiteSpace(data))
                return DecodeBase64Url(data);
        }

        if (part.Parts == null)
            return string.Empty;

        foreach (var child in part.Parts)
        {
            var value = ExtractMimeBody(child, mimeType);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static bool HasAttachments(GmailMessagePart? payload)
    {
        if (payload == null)
            return false;

        if (!string.IsNullOrWhiteSpace(payload.Filename))
            return true;

        if (payload.Parts == null)
            return false;

        foreach (var part in payload.Parts)
        {
            if (HasAttachments(part))
                return true;
        }

        return false;
    }

    private static DateTime? ParseInternalDate(string? internalDate)
    {
        if (!long.TryParse(internalDate, out var ms))
            return null;

        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }

    private static string DecodeBase64Url(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        var remainder = normalized.Length % 4;
        if (remainder > 0)
            normalized = normalized.PadRight(normalized.Length + (4 - remainder), '=');

        var bytes = Convert.FromBase64String(normalized);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

internal class GoogleOAuthSettingsDocument
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Email { get; set; }
    public string? RefreshToken { get; set; }
}

internal class GoogleOAuthAccessToken
{
    public string Email { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}

internal class GmailListMessagesResponse
{
    public List<GmailMessageListItem>? Messages { get; set; }
    public string? NextPageToken { get; set; }
}

internal class GmailMessageListItem
{
    public string? Id { get; set; }
}

internal class GmailMessageResponse
{
    public string? Id { get; set; }
    public string? Snippet { get; set; }
    public string? InternalDate { get; set; }
    public GmailMessagePart? Payload { get; set; }
}

internal class GmailMessagePart
{
    public string? MimeType { get; set; }
    public string? Filename { get; set; }
    public GmailMessagePartBody? Body { get; set; }
    public List<GmailHeader>? Headers { get; set; }
    public List<GmailMessagePart>? Parts { get; set; }
}

internal class GmailMessagePartBody
{
    public string? Data { get; set; }
}

internal class GmailHeader
{
    public string? Name { get; set; }
    public string? Value { get; set; }
}

internal class GmailCursor
{
    public string? LastMessageId { get; set; }
    public long? LastInternalDateMs { get; set; }
}
