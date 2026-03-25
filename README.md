# Quaero
Personal, local-only indexing and search tool. Allows you to store and index your data locally, making it easily searchable across your multiple devices.

## Architecture

Quaero is a cross-platform C# solution (.NET 10) with a plugin-based architecture:

| Project | Description |
|---------|-------------|
| **Quaero.Core** | Core library: data models, SQLite storage with FTS5 full-text search, AES-256 encryption, indexing service |
| **Quaero.Plugins.Abstractions** | Plugin interface (`ISearchPlugin`) and data contracts |
| **Quaero.Plugins.Markdown** | Indexes `.md` files using Markdig — extracts title from H1, summary from first paragraph |
| **Quaero.Plugins.Text** | Indexes `.txt` files — uses filename as title, first 500 chars as summary |
| **Quaero.Plugins.Json** | Indexes `.json` files — intelligently extracts title/summary from common JSON field names |
| **Quaero.Plugins.Imap** | Indexes emails via IMAP (Gmail, Outlook, etc.) using MailKit |
| **Quaero.Indexer** | Background service that periodically runs all plugins and indexes content |
| **Quaero.UI** | Avalonia desktop app for searching, viewing results, and managing the index |

## Indexed Document Model

Each document in the index contains:
- **Machine** — originating computer name
- **Type** — source type (markdown, text, json, email)
- **Provider** — data source (local-files, gmail, etc.)
- **Location** — path or URL to the original content
- **Title** / **Summary** / **Content** — searchable text fields
- **ExtendedData** — key-value metadata specific to the source
- **ContentHash** — for change detection and deduplication

## Getting Started

### Build
```bash
dotnet build
```

### Run the Indexer
Configure plugins in `%LOCALAPPDATA%/Quaero/plugins.json`:
```json
{
  "quaero.plugins.markdown": {
    "Enabled": true,
    "Settings": {
      "Directories": "C:\\Users\\me\\Documents;C:\\Users\\me\\Notes"
    }
  },
  "quaero.plugins.text": {
    "Enabled": true,
    "Settings": {
      "Directories": "C:\\Users\\me\\Documents"
    }
  },
  "quaero.plugins.json": {
    "Enabled": true,
    "Settings": {
      "Directories": "C:\\Users\\me\\Data"
    }
  },
  "quaero.plugins.imap": {
    "Enabled": false,
    "Settings": {
      "Host": "imap.gmail.com",
      "Port": "993",
      "UseSsl": "true",
      "Username": "you@gmail.com",
      "Password": "your-app-password",
      "Provider": "gmail",
      "MaxMessages": "500"
    }
  }
}
```

Then run:
```bash
dotnet run --project src/Quaero.Indexer
```

### Run the UI
```bash
dotnet run --project src/Quaero.UI
```

## Features

- **Full-text search** via SQLite FTS5
- **Plugin architecture** — drop-in extensibility for new data sources
- **Optional encryption** — AES-256 content encryption at rest
- **Index compaction** — VACUUM support for minimal storage
- **Cross-platform** — runs on Windows, macOS, and Linux
