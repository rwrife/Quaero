using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quaero.Core.Models;

namespace Quaero.Core.Storage;

/// <summary>
/// SQLite-backed index store with FTS5 full-text search and optional encryption.
/// </summary>
public class IndexStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IndexConfiguration _config;
    private bool _disposed;

    public IndexStore(IndexConfiguration config)
    {
        _config = config;

        var dir = Path.GetDirectoryName(config.DatabasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = config.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        _connection = new SqliteConnection(connStr);
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                machine TEXT NOT NULL,
                type TEXT NOT NULL,
                provider TEXT NOT NULL,
                location TEXT NOT NULL,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                content TEXT NOT NULL,
                extended_data TEXT NOT NULL DEFAULT '{}',
                indexed_at TEXT NOT NULL,
                content_hash TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_documents_provider ON documents(provider);
            CREATE INDEX IF NOT EXISTS idx_documents_type ON documents(type);
            CREATE INDEX IF NOT EXISTS idx_documents_machine ON documents(machine);
            CREATE INDEX IF NOT EXISTS idx_documents_hash ON documents(content_hash);
            """;
        cmd.ExecuteNonQuery();

        // Create FTS5 virtual table if it doesn't exist
        using var ftsCmd = _connection.CreateCommand();
        ftsCmd.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(
                id UNINDEXED,
                title,
                summary,
                content
            );
            """;
        ftsCmd.ExecuteNonQuery();
    }

    public async Task UpsertDocumentAsync(IndexedDocument doc, CancellationToken cancellationToken = default)
    {
        var extendedJson = JsonSerializer.Serialize(doc.ExtendedData);
        var content = _config.EncryptionEnabled && _config.EncryptionKey != null
            ? Encrypt(doc.Content, _config.EncryptionKey)
            : doc.Content;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (id, machine, type, provider, location, title, summary, content, extended_data, indexed_at, content_hash)
            VALUES ($id, $machine, $type, $provider, $location, $title, $summary, $content, $extended, $indexed_at, $hash)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                summary = excluded.summary,
                content = excluded.content,
                extended_data = excluded.extended_data,
                indexed_at = excluded.indexed_at,
                content_hash = excluded.content_hash;
            """;
        cmd.Parameters.AddWithValue("$id", doc.Id);
        cmd.Parameters.AddWithValue("$machine", doc.Machine);
        cmd.Parameters.AddWithValue("$type", doc.Type);
        cmd.Parameters.AddWithValue("$provider", doc.Provider);
        cmd.Parameters.AddWithValue("$location", doc.Location);
        cmd.Parameters.AddWithValue("$title", doc.Title);
        cmd.Parameters.AddWithValue("$summary", doc.Summary);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$extended", extendedJson);
        cmd.Parameters.AddWithValue("$indexed_at", doc.IndexedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$hash", doc.ContentHash);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Update FTS index
        using var ftsCmd = _connection.CreateCommand();
        ftsCmd.CommandText = """
            INSERT OR REPLACE INTO documents_fts (id, title, summary, content)
            VALUES ($id, $title, $summary, $content);
            """;
        ftsCmd.Parameters.AddWithValue("$id", doc.Id);
        ftsCmd.Parameters.AddWithValue("$title", doc.Title);
        ftsCmd.Parameters.AddWithValue("$summary", doc.Summary);
        ftsCmd.Parameters.AddWithValue("$content", doc.Content); // FTS always uses plaintext for search
        await ftsCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var results = new List<SearchResult>();

        using var cmd = _connection.CreateCommand();
        var conditions = new List<string>();
        var hasFullText = !string.IsNullOrWhiteSpace(query.QueryText);

        string sql;
        if (hasFullText)
        {
            sql = """
                SELECT d.*, fts.rank
                FROM documents_fts fts
                JOIN documents d ON d.id = fts.id
                WHERE documents_fts MATCH $query
                """;
            cmd.Parameters.AddWithValue("$query", query.QueryText);
        }
        else
        {
            sql = "SELECT d.*, 0 as rank FROM documents d WHERE 1=1";
        }

        if (query.Provider != null)
        {
            sql += " AND d.provider = $provider";
            cmd.Parameters.AddWithValue("$provider", query.Provider);
        }
        if (query.Type != null)
        {
            sql += " AND d.type = $type";
            cmd.Parameters.AddWithValue("$type", query.Type);
        }
        if (query.Machine != null)
        {
            sql += " AND d.machine = $machine";
            cmd.Parameters.AddWithValue("$machine", query.Machine);
        }

        if (hasFullText)
            sql += " ORDER BY rank";
        else
            sql += " ORDER BY d.indexed_at DESC";

        sql += " LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", query.MaxResults);
        cmd.Parameters.AddWithValue("$offset", query.Offset);

        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var doc = ReadDocument(reader);
            var rank = reader.GetDouble(reader.GetOrdinal("rank"));
            results.Add(new SearchResult { Document = doc, Rank = rank });
        }

        return results;
    }

    public async Task<IndexedDocument?> GetByLocationAsync(string location, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM documents WHERE location = $location";
        cmd.Parameters.AddWithValue("$location", location);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return ReadDocument(reader);
        return null;
    }

    public async Task<bool> HasChangedAsync(string location, string contentHash, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM documents WHERE location = $location";
        cmd.Parameters.AddWithValue("$location", location);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null) return true;
        return result.ToString() != contentHash;
    }

    public async Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM documents";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<List<string>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        var providers = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT provider FROM documents ORDER BY provider";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            providers.Add(reader.GetString(0));
        return providers;
    }

    public void Compact()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "VACUUM;";
        cmd.ExecuteNonQuery();
    }

    private IndexedDocument ReadDocument(SqliteDataReader reader)
    {
        var content = reader.GetString(reader.GetOrdinal("content"));
        if (_config.EncryptionEnabled && _config.EncryptionKey != null)
        {
            try { content = Decrypt(content, _config.EncryptionKey); }
            catch { /* content may not be encrypted */ }
        }

        return new IndexedDocument
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Machine = reader.GetString(reader.GetOrdinal("machine")),
            Type = reader.GetString(reader.GetOrdinal("type")),
            Provider = reader.GetString(reader.GetOrdinal("provider")),
            Location = reader.GetString(reader.GetOrdinal("location")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Summary = reader.GetString(reader.GetOrdinal("summary")),
            Content = content,
            ExtendedData = JsonSerializer.Deserialize<Dictionary<string, string>>(
                reader.GetString(reader.GetOrdinal("extended_data"))) ?? new(),
            IndexedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("indexed_at"))),
            ContentHash = reader.GetString(reader.GetOrdinal("content_hash"))
        };
    }

    private static string Encrypt(string plainText, string key)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey(key);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return Convert.ToBase64String(result);
    }

    private static string Decrypt(string cipherText, string key)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = DeriveKey(key);

        var iv = new byte[aes.BlockSize / 8];
        var cipher = new byte[fullCipher.Length - iv.Length];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DeriveKey(string key)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
