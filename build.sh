#!/usr/bin/env bash
# Builds pc-tool's CLI and GUI as self-contained single-file binaries for the
# current OS and copies them into output/. Run on Linux/Mac (see build.ps1
# for Windows).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PC_TOOL_DIR="$SCRIPT_DIR/pc-tool"
OUTPUT_DIR="$SCRIPT_DIR/output"

case "$(uname -s)" in
    Linux*) RID=linux-x64 ;;
    Darwin*)
        if [ "$(uname -m)" = "arm64" ]; then RID=osx-arm64; else RID=osx-x64; fi
        ;;
    *) echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
esac

echo "== Building for $RID =="
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

build_project() {
    local project="$1"
    local name="$2"
    local tmp_dir
    tmp_dir="$(mktemp -d)"

    echo "-- $name ($project) --"
    dotnet publish "$project" \
        -c Release \
        -r "$RID" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$tmp_dir"

    cp "$tmp_dir/$name" "$OUTPUT_DIR/$name"
    rm -rf "$tmp_dir"
}

build_project "$PC_TOOL_DIR/SwitchCloudSaveBrew.Cli/SwitchCloudSaveBrew.Cli.csproj" "SwitchCloudSaveBrew.Cli"
build_project "$PC_TOOL_DIR/SwitchCloudSaveBrew.Gui/SwitchCloudSaveBrew.Gui.csproj" "SwitchCloudSaveBrew.Gui"

echo "== Done: $OUTPUT_DIR =="
ls -la "$OUTPUT_DIR"
