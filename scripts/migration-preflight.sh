#!/usr/bin/env bash
# Pre-flight checks for the migration cron worker.
# Validates that previously closed [migration N/M] issues' acceptance still holds:
#   - npm install succeeds
#   - npm run build succeeds (if any TS exists)
#   - npm test succeeds (only if at least one *.test.ts exists)
# Prints a JSON status object on stdout. Non-zero exit if a hard failure occurred.
set -uo pipefail
cd "$(dirname "$0")/.."

status=ok
notes=()

emit() {
  printf '{"status":"%s","branch":"%s","notes":%s,"nextIssue":%s}\n' \
    "$1" "$(git rev-parse --abbrev-ref HEAD)" \
    "$(printf '%s\n' "${notes[@]:-}" | jq -R . | jq -s .)" \
    "${2:-null}"
}

if ! command -v npm >/dev/null; then
  notes+=("npm not found"); emit error; exit 1
fi

# git sync
git fetch origin --quiet || notes+=("git fetch failed")
git checkout node-migration --quiet 2>/dev/null || git checkout -b node-migration origin/node-migration --quiet 2>/dev/null || notes+=("checkout node-migration failed")
git pull --rebase origin node-migration --quiet 2>/dev/null || notes+=("pull failed")

if [ ! -f package.json ]; then
  notes+=("no package.json"); status=error
fi

if [ "$status" = "ok" ]; then
  npm install --no-audit --no-fund --silent 2>install.log || { status=error; notes+=("npm install failed: $(tail -3 install.log | tr '\n' ' ')"); }
fi

if [ "$status" = "ok" ] && [ -f tsconfig.json ]; then
  npm run build --silent 2>build.log || { status=error; notes+=("build failed: $(tail -5 build.log | tr '\n' ' ')"); }
fi

if [ "$status" = "ok" ] && find packages -name '*.test.ts' -print -quit | grep -q .; then
  npm test --silent 2>test.log || { status=error; notes+=("tests failed: $(tail -5 test.log | tr '\n' ' ')"); }
fi

# Find next open migration issue (lowest N).
next_json=$(gh issue list --label node-migration --state open --json number,title --jq \
  'map(select(.title|test("\\[migration "))) | sort_by(.title) | .[0] // null' 2>/dev/null || echo null)

emit "$status" "$next_json"
