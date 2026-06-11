#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK_PROJECT="$ROOT_DIR/sdk/PKHeX/PKHeX.Core/PKHeX.Core.csproj"
SDK_PROPS="$ROOT_DIR/sdk/PKHeX/Directory.Build.props"
ROOT_PROPS="$ROOT_DIR/server/PkManager.Server/Directory.Build.props"
CONFIG="${1:-Release}"
FEED_DIR="$ROOT_DIR/server/artifacts/nuget"

if [ ! -f "$SDK_PROJECT" ]; then
    echo "[ERROR] PKHeX.Core source project not found: $SDK_PROJECT" >&2
    exit 1
fi

if [ ! -f "$ROOT_PROPS" ]; then
    echo "[ERROR] Directory.Build.props not found: $ROOT_PROPS" >&2
    exit 1
fi

sdk_version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SDK_PROPS" | head -n 1)"
if [ -z "$sdk_version" ]; then
    echo "[ERROR] Unable to read PKHeX SDK version from $SDK_PROPS" >&2
    exit 1
fi

normalize_version() {
    local version="$1"
    local IFS='.'
    read -r -a parts <<< "$version"
    local out=()

    for part in "${parts[@]}"; do
        if [[ "$part" =~ ^([0-9]+)(.*)$ ]]; then
            out+=("$((10#${BASH_REMATCH[1]}))${BASH_REMATCH[2]}")
        else
            out+=("$part")
        fi
    done

    local normalized="${out[0]}"
    local i
    for ((i = 1; i < ${#out[@]}; i++)); do
        normalized+=".${out[i]}"
    done
    printf '%s' "$normalized"
}

package_version="$(normalize_version "$sdk_version")"
package_file="$FEED_DIR/PKHeX.Core.$package_version.nupkg"

mkdir -p "$FEED_DIR"
dotnet pack "$SDK_PROJECT" -c "$CONFIG" -o "$FEED_DIR" /p:Version="$package_version"

if [ ! -f "$package_file" ]; then
    echo "[ERROR] PKHeX.Core package was not produced: $package_file" >&2
    exit 1
fi

tmp_props="$(mktemp)"
sed "s:<PKHeXCoreVersion>.*</PKHeXCoreVersion>:<PKHeXCoreVersion>$package_version</PKHeXCoreVersion>:" "$ROOT_PROPS" > "$tmp_props"
mv "$tmp_props" "$ROOT_PROPS"

printf '%s\n' "$package_version" > "$FEED_DIR/VERSION.txt"

echo "[INFO] PKHeX.Core package $package_version published to $FEED_DIR"
