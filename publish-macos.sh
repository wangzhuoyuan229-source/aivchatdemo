#!/usr/bin/env bash
set -euo pipefail

case "$(uname -m)" in
  arm64) runtime_id="osx-arm64" ;;
  x86_64) runtime_id="osx-x64" ;;
  *) echo "Unsupported macOS architecture: $(uname -m)" >&2; exit 1 ;;
esac

project_dir="$(cd "$(dirname "$0")" && pwd)"
output_dir="$project_dir/publish/$runtime_id"
raw_dir="$output_dir/raw"
app_dir="$output_dir/ChatApp.app"

dotnet publish "$project_dir/ChatApp.UI/ChatApp.UI.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$raw_dir"

mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"
cp -R "$raw_dir/." "$app_dir/Contents/MacOS/"
cp "$project_dir/ChatApp.UI/Platforms/macOS/Info.plist" "$app_dir/Contents/Info.plist"
chmod +x "$app_dir/Contents/MacOS/ChatApp.UI"
find "$app_dir/Contents/MacOS" -type f -name '*.pdb' -delete
xattr -cr "$app_dir" 2>/dev/null || true

echo "Created $app_dir"
