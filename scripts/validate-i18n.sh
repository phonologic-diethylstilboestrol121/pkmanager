#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v node >/dev/null 2>&1; then
  echo "node is required" >&2
  exit 1
fi

PKMANAGER_ROOT="$ROOT" node - <<'NODE'
const fs = require('fs');
const path = require('path');

const root = process.env.PKMANAGER_ROOT;
const frontendLocalesRoot = path.join(root, 'client', 'src', 'i18n', 'locales');
const backendResourcesRoot = path.join(root, 'server', 'PkManager.Server', 'Resources');
const backendSourceRoot = path.join(root, 'server', 'PkManager.Server');
const canonicalFrontendLang = 'zh-Hans';
const canonicalBackendFile = 'Messages.zh-Hans.json';
const frontendNamespaces = ['common', 'messages', 'pages', 'editor', 'games', 'emulator'];

let errorCount = 0;
let warningCount = 0;

function logError(message) {
  errorCount += 1;
  console.error(`ERROR ${message}`);
}

function logWarning(message) {
  warningCount += 1;
  console.warn(`WARN ${message}`);
}

function formatList(items, limit = 12) {
  if (items.length <= limit) {
    return items.join(', ');
  }
  return `${items.slice(0, limit).join(', ')} ... (+${items.length - limit} more)`;
}

function ensureDirectory(dirPath, label) {
  if (!fs.existsSync(dirPath) || !fs.statSync(dirPath).isDirectory()) {
    logError(`${label} directory not found: ${dirPath}`);
    return false;
  }
  return true;
}

function parseJson(filePath, label) {
  try {
    const raw = fs.readFileSync(filePath, 'utf8');
    return JSON.parse(raw);
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    logError(`${label} is not valid JSON: ${reason}`);
    return null;
  }
}

function flattenKeys(value, prefix = '', output = []) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    if (prefix) {
      output.push(prefix);
    }
    return output;
  }

  const keys = Object.keys(value).sort();
  if (keys.length === 0 && prefix) {
    output.push(prefix);
    return output;
  }

  for (const key of keys) {
    const nextPrefix = prefix ? `${prefix}.${key}` : key;
    flattenKeys(value[key], nextPrefix, output);
  }

  return output;
}

function compareKeySets(label, canonicalKeys, targetKeys) {
  const canonicalSet = new Set(canonicalKeys);
  const targetSet = new Set(targetKeys);
  const missing = canonicalKeys.filter((key) => !targetSet.has(key));
  const extra = targetKeys.filter((key) => !canonicalSet.has(key));

  if (missing.length > 0) {
    logError(`${label} is missing ${missing.length} key(s): ${formatList(missing)}`);
  }

  if (extra.length > 0) {
    logWarning(`${label} has ${extra.length} extra key(s): ${formatList(extra)}`);
  }
}

function validateFrontendLocales() {
  if (!ensureDirectory(frontendLocalesRoot, 'Frontend locales')) {
    return;
  }

  const langDirs = fs.readdirSync(frontendLocalesRoot, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();

  if (!langDirs.includes(canonicalFrontendLang)) {
    logError(`Canonical frontend locale "${canonicalFrontendLang}" is missing`);
    return;
  }

  const canonicalDir = path.join(frontendLocalesRoot, canonicalFrontendLang);
  const canonicalKeysByNamespace = new Map();

  for (const namespace of frontendNamespaces) {
    const fileName = `${namespace}.json`;
    const filePath = path.join(canonicalDir, fileName);
    if (!fs.existsSync(filePath)) {
      logError(`Canonical frontend namespace file missing: ${filePath}`);
      continue;
    }

    const data = parseJson(filePath, filePath);
    if (data === null) {
      continue;
    }
    if (typeof data !== 'object' || data === null || Array.isArray(data)) {
      logError(`${filePath} must contain a JSON object`);
      continue;
    }

    canonicalKeysByNamespace.set(namespace, flattenKeys(data));
  }

  for (const lang of langDirs) {
    const langDir = path.join(frontendLocalesRoot, lang);
    const actualFiles = fs.readdirSync(langDir, { withFileTypes: true })
      .filter((entry) => entry.isFile() && entry.name.endsWith('.json'))
      .map((entry) => entry.name)
      .sort();

    const expectedFiles = frontendNamespaces.map((namespace) => `${namespace}.json`);
    const extraFiles = actualFiles.filter((fileName) => !expectedFiles.includes(fileName));
    if (extraFiles.length > 0) {
      logWarning(`${langDir} has unexpected locale file(s): ${formatList(extraFiles)}`);
    }

    for (const namespace of frontendNamespaces) {
      const fileName = `${namespace}.json`;
      const filePath = path.join(langDir, fileName);

      if (!fs.existsSync(filePath)) {
        logError(`${filePath} is missing`);
        continue;
      }

      const data = parseJson(filePath, filePath);
      if (data === null) {
        continue;
      }
      if (typeof data !== 'object' || data === null || Array.isArray(data)) {
        logError(`${filePath} must contain a JSON object`);
        continue;
      }

      const canonicalKeys = canonicalKeysByNamespace.get(namespace);
      if (!canonicalKeys) {
        continue;
      }

      compareKeySets(filePath, canonicalKeys, flattenKeys(data));
    }
  }
}

function validateBackendMessages() {
  if (!ensureDirectory(backendResourcesRoot, 'Backend resources')) {
    return;
  }

  const files = fs.readdirSync(backendResourcesRoot, { withFileTypes: true })
    .filter((entry) => entry.isFile() && /^Messages\..+\.json$/.test(entry.name))
    .map((entry) => entry.name)
    .sort();

  if (!files.includes(canonicalBackendFile)) {
    logError(`Canonical backend message file "${canonicalBackendFile}" is missing`);
    return;
  }

  const canonicalPath = path.join(backendResourcesRoot, canonicalBackendFile);
  const canonicalData = parseJson(canonicalPath, canonicalPath);
  if (canonicalData === null) {
    return;
  }
  if (typeof canonicalData !== 'object' || canonicalData === null || Array.isArray(canonicalData)) {
    logError(`${canonicalPath} must contain a JSON object`);
    return;
  }

  const canonicalKeys = flattenKeys(canonicalData);
  for (const fileName of files) {
    const filePath = path.join(backendResourcesRoot, fileName);
    const data = parseJson(filePath, filePath);
    if (data === null) {
      continue;
    }
    if (typeof data !== 'object' || data === null || Array.isArray(data)) {
      logError(`${filePath} must contain a JSON object`);
      continue;
    }

    if (fileName === canonicalBackendFile) {
      continue;
    }

    compareKeySets(filePath, canonicalKeys, flattenKeys(data));
  }

  validateBackendMessageUsage(canonicalKeys);
}

function collectFiles(dirPath, predicate, output = []) {
  for (const entry of fs.readdirSync(dirPath, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj' || entry.name === 'Resources') {
      continue;
    }

    const fullPath = path.join(dirPath, entry.name);
    if (entry.isDirectory()) {
      collectFiles(fullPath, predicate, output);
      continue;
    }

    if (entry.isFile() && predicate(fullPath)) {
      output.push(fullPath);
    }
  }

  return output;
}

function validateBackendMessageUsage(canonicalKeys) {
  const canonicalSet = new Set(canonicalKeys);
  const codeFiles = collectFiles(backendSourceRoot, (filePath) => filePath.endsWith('.cs'));
  const patterns = [
    /FromKey(?:WithFallback)?\(\s*"([^"]+)"/g,
    /OkMessage(?:Fallback)?\([^,]+,\s*"([^"]+)"/g,
    /ErrorMessage(?:Fallback)?(?:<[^>]+>)?\([^,]+,\s*"([^"]+)"/g,
    /_messages\.Get\("([^"]+)"/g,
    /ApiResponse<[^>]+>\.(?:Ok|Error)\([^,\n]+,\s*_messages\.Get\("([^"]+)"/g
  ];

  const usedKeys = new Set();
  for (const filePath of codeFiles) {
    const text = fs.readFileSync(filePath, 'utf8');
    for (const pattern of patterns) {
      let match;
      while ((match = pattern.exec(text)) !== null) {
        usedKeys.add(match[1]);
      }
    }
  }

  const missing = [...usedKeys].filter((key) => !canonicalSet.has(key)).sort();
  if (missing.length > 0) {
    logError(`Backend message key(s) referenced in code but missing from ${canonicalBackendFile}: ${formatList(missing)}`);
  }
}

validateFrontendLocales();
validateBackendMessages();

if (errorCount > 0) {
  console.error(`Validation failed with ${errorCount} error(s) and ${warningCount} warning(s).`);
  process.exit(1);
}

const summary = `Validation passed with ${warningCount} warning(s).`;
if (warningCount > 0) {
  console.warn(summary);
} else {
  console.log(summary);
}
NODE
