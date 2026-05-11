#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Issues are created in plan order. Each title is prefixed with [migration N/M]
# so a cron worker can pick the next OPEN one easily.

declare -a TITLES=(
  "[migration 1/9] Scaffold Node.js workspaces, tooling, and CI baseline"
  "[migration 2/9] Implement @quaero/plugin-api package"
  "[migration 3/9] Implement @quaero/core (storage, plugin loader, indexing service)"
  "[migration 4/9] Port file-based plugins (markdown, text, json) to Node"
  "[migration 5/9] Port IMAP/Gmail plugin to Node (imapflow + mailparser)"
  "[migration 6/9] Build @quaero/daemon (Fastify HTTP API + scheduler)"
  "[migration 7/9] Build @quaero/cli (Ink TUI + sub-commands)"
  "[migration 8/9] Add Vitest unit tests across all packages"
  "[migration 9/9] Update GitHub Actions CI for Node matrix and remove .NET workflow refs"
)

declare -a BODIES=(
"## Goal
Stand up the Node.js monorepo so all subsequent migration steps have a place to land.

## Acceptance criteria
- [x] Root \`package.json\` declares npm workspaces under \`packages/*\`
- [x] Root \`tsconfig.json\` with project references for every package
- [x] Vitest config, Prettier, and \`.gitignore\` configured
- [x] All C# source removed (\`src/\`, \`Quaero.slnx\`)
- [ ] \`npm install\` succeeds at the repo root
- [ ] \`npm run build\` succeeds (pre-tests)

Status: implemented in initial commit on the \`node-migration\` branch — close after verifying \`npm install && npm run build\` work in CI."

"## Goal
Provide the cross-package \`ISearchPlugin\` contract.

## Acceptance criteria
- [x] \`@quaero/plugin-api\` package compiles standalone
- [x] Exports \`ISearchPlugin\`, \`PluginMetadata\`, \`PluginConfiguration\`, \`DiscoveredDocument\`, \`PluginSettingDescriptor\`, \`PluginSettingType\`
- [ ] Type tests / structural compatibility verified by \`npm run build\`

Mirrors C# \`Quaero.Plugins.Abstractions\` semantics with idiomatic TS (async iterables, AbortSignal)."

"## Goal
Port \`Quaero.Core\` to Node: storage, encryption, run log, dynamic plugin loading, scheduler.

## Acceptance criteria
- [x] \`IndexStore\` on \`better-sqlite3\` with FTS5 virtual table, AES-256-GCM encryption hooks, identical query surface
- [x] \`DataSourceStore\` JSON persistence
- [x] \`PluginLoader\` resolves plugins from \`pluginsDirectory\` (sub-packages, \`.plugin.js\` files, \`plugins.json\` manifest) and from installed \`node_modules\`
- [x] \`IndexingService\` with cron evaluation (\`croner\`), incremental indexing via last successful run, hash-based skip, structured run log
- [x] Config load/save helpers
- [ ] Build green; smoke-tested by tests in step 8"

"## Goal
Re-implement the file-based plugin packages.

## Acceptance criteria
- [ ] \`@quaero/plugin-markdown\` — uses \`markdown-it\` (or \`remark\`) to extract H1 title and first paragraph summary; glob via \`fast-glob\`
- [ ] \`@quaero/plugin-text\` — filename as title, summary truncation, last-modified incremental skip
- [ ] \`@quaero/plugin-json\` — dot-path mappings + auto-detect fallback
- [ ] All three respect \`PluginConfiguration.lastSuccessfulRun\` for incremental runs
- [ ] Unit tests (in step 8) cover each plugin against tmp directories"

"## Goal
Re-implement the IMAP plugin (covers Gmail through standard IMAP).

## Acceptance criteria
- [ ] \`@quaero/plugin-imap\` using \`imapflow\` + \`mailparser\`
- [ ] Settings parity with .NET version (Host/Port/UseSsl/Username/Password/Provider/MaxMessages)
- [ ] Incremental indexing via \`SINCE\` IMAP search using \`lastSuccessfulRun\`
- [ ] HTML→text fallback when only HTML body available
- [ ] Unit test coverage for the body/extended-data shaping (mocked client)"

"## Goal
Background daemon: HTTP API + cron scheduler loop.

## Acceptance criteria
- [ ] Fastify server with routes from the C# version: \`GET /\`, \`GET /api/config\`, \`PUT /api/config/server-base-url\`, \`GET /api/plugins\`
- [ ] New routes: \`GET /api/datasources\`, \`POST /api/datasources\`, \`PUT /api/datasources/:id\`, \`DELETE /api/datasources/:id\`, \`POST /api/datasources/:id/run\`, \`GET /api/datasources/:id/runs\`, \`POST /api/search\`
- [ ] 1-minute scheduler loop calling \`IndexingService.evaluateAndRun\`
- [ ] Graceful shutdown on SIGINT/SIGTERM
- [ ] Bundled binary entry \`bin/quaero-daemon\`"

"## Goal
Replace the Avalonia UI with an Ink-powered CLI.

## Acceptance criteria
- [ ] Default \`quaero\` command launches an interactive Ink TUI with Search and Data Sources views
- [ ] Sub-commands:
  - \`quaero search <query>\` — talks to daemon, prints results
  - \`quaero ds list|add|edit|remove|run|runs\`
  - \`quaero daemon start|stop|status\` (spawns/forks the daemon)
  - \`quaero plugins list\`
- [ ] Bundled binary entry \`bin/quaero\`
- [ ] Works on macOS / Linux / Windows terminals"

"## Goal
Comprehensive Vitest coverage that validates ported behavior.

## Acceptance criteria
- [ ] crypto round-trip (encrypt → decrypt) and tamper rejection
- [ ] \`IndexStore\` upsert + FTS search + provider/type filters + run log lifecycle
- [ ] \`hasChanged\` and \`ensureDataSourceMetadataByLocation\`
- [ ] \`IndexingService.isDue\` cron logic
- [ ] \`PluginLoader\` resolving local file plugin
- [ ] Markdown, text, json plugins discover documents from a tmp directory
- [ ] CI runs \`npm test\` and fails the build on regressions"

"## Goal
GitHub Actions for Node.

## Acceptance criteria
- [ ] \`.github/workflows/ci.yml\` running on \`push\` + \`pull_request\` to \`main\`
- [ ] Matrix: \`{ ubuntu-latest, macos-latest, windows-latest } x { node-version: [20.x, 22.x] }\`
- [ ] Steps: checkout → setup-node (cache npm) → \`npm ci\` → \`npm run build\` → \`npm test\`
- [ ] Remove any leftover .NET workflows / configs
- [ ] README updated with build/run instructions"
)

for i in "${!TITLES[@]}"; do
  title="${TITLES[$i]}"
  body="${BODIES[$i]}"
  if gh issue list --label node-migration --state all --search "$title in:title" --json title --jq '.[].title' | grep -Fxq "$title"; then
    echo "exists: $title"
  else
    gh issue create --title "$title" --body "$body" --label node-migration | tail -1
  fi
done
