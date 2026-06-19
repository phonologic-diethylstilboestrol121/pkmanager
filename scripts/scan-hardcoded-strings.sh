#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STRICT=0
BASELINE_FILE=""

usage() {
  cat <<'EOF'
Usage: scan-hardcoded-strings.sh [--strict] [--baseline <file>]

Scans frontend/backend source files for Chinese characters that are not already
wrapped in the current i18n fallback patterns.

Options:
  --strict   Exit with status 1 when any potential hardcoded string is found.
  --baseline Compare against a baseline file and report only new matches.
  -h, --help Show this help text.
EOF
}

while (($# > 0)); do
  case "$1" in
    --strict)
      STRICT=1
      shift
      ;;
    --baseline)
      if (($# < 2)); then
        echo "--baseline requires a file path" >&2
        usage >&2
        exit 2
      fi
      BASELINE_FILE="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

for cmd in rg node; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "$cmd is required" >&2
    exit 1
  fi
done

TMP_FILE="$(mktemp)"
trap 'rm -f "$TMP_FILE"' EXIT

rg -n --no-heading --color never '[一-龥]' \
  "$ROOT/client/src" \
  "$ROOT/server/PkManager.Server" \
  --glob '*.ts' \
  --glob '*.tsx' \
  --glob '*.cs' \
  --glob '!**/bin/**' \
  --glob '!**/obj/**' \
  --glob '!**/public/**' \
  --glob '!**/i18n/locales/**' \
  --glob '!**/Resources/**' \
  --glob '!**/*.min.js' \
  >"$TMP_FILE" || true

SCAN_STRICT="$STRICT" SCAN_BASELINE_FILE="$BASELINE_FILE" node - "$TMP_FILE" <<'NODE'
const fs = require('fs');

const strict = process.env.SCAN_STRICT === '1';
const baselineFile = process.env.SCAN_BASELINE_FILE || '';
const inputPath = process.argv[2];
const raw = fs.existsSync(inputPath) ? fs.readFileSync(inputPath, 'utf8') : '';
const lines = raw.length > 0 ? raw.split(/\r?\n/).filter(Boolean) : [];

function splitMatch(line) {
  const match = line.match(/^(.*?):(\d+):(.*)$/);
  if (!match) {
    return null;
  }
  return { file: match[1], line: match[2], content: match[3] };
}

function findCommentStart(content) {
  let quote = null;
  let escape = false;

  for (let i = 0; i < content.length - 1; i += 1) {
    const ch = content[i];
    const next = content[i + 1];

    if (quote !== null) {
      if (escape) {
        escape = false;
        continue;
      }
      if (ch === '\\') {
        escape = true;
        continue;
      }
      if (ch === quote) {
        quote = null;
      }
      continue;
    }

    if (ch === '"' || ch === "'" || ch === '`') {
      quote = ch;
      continue;
    }

    if (ch === '/' && next === '/') {
      return i;
    }

    if (ch === '/' && next === '*') {
      return i;
    }
  }

  return -1;
}

function hasChinese(text) {
  return /[一-龥]/.test(text);
}

function isI18nFallback(content) {
  if (/defaultValue\s*:/.test(content)) {
    return true;
  }

  if (/\b[A-Za-z_$][\w$]*\(\s*['"`][^'"`]+['"`]\s*,\s*['"`]/.test(content)) {
    return true;
  }

  if (/\bgetI18nText\(/.test(content)) {
    return true;
  }

  return false;
}

function shouldIgnore({ content }) {
  if (!content.trim()) {
    return true;
  }

  if (content.includes('i18n-ignore')) {
    return true;
  }

  const trimmed = content.trimStart();
  if (/^(?:\/\/\/|\/\/|\/\*|\*\/|\*)/.test(trimmed)) {
    return true;
  }

  if (/^(?:import|export)\b/.test(trimmed)) {
    return true;
  }

  const commentStart = findCommentStart(content);
  if (commentStart >= 0) {
    const codePart = content.slice(0, commentStart);
    const commentPart = content.slice(commentStart);
    if (!hasChinese(codePart) && hasChinese(commentPart)) {
      return true;
    }
  }

  if (isI18nFallback(content)) {
    return true;
  }

  return false;
}

const matches = [];
for (const line of lines) {
  const parsed = splitMatch(line);
  if (!parsed || shouldIgnore(parsed)) {
    continue;
  }
  matches.push(line);
}

let baseline = null;
if (baselineFile) {
  if (!fs.existsSync(baselineFile)) {
    console.error(`Baseline file not found: ${baselineFile}`);
    process.exit(2);
  }
  baseline = new Set(
    fs.readFileSync(baselineFile, 'utf8')
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean)
  );
}

const filteredMatches = baseline
  ? matches.filter((line) => !baseline.has(line))
  : matches;

for (const match of filteredMatches) {
  console.log(match);
}

const summaryLabel = baseline ? 'new hardcoded string match' : 'potential hardcoded string match';
const summary = `Found ${filteredMatches.length} ${summaryLabel}${filteredMatches.length === 1 ? '' : 'es'}.`;
if (filteredMatches.length > 0) {
  console.error(summary);
} else {
  console.log(summary);
}

if (strict && filteredMatches.length > 0) {
  process.exit(1);
}
NODE
