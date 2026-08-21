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

# Always build a clean bundle so removed assemblies/resources cannot leak from
# a previous release into the new archive.
rm -rf "$raw_dir" "$app_dir"

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

if [[ ! -d "$raw_dir/BundledKnowledge" ]]; then
  echo "Bundled knowledge was not copied to the publish output." >&2
  exit 1
fi

mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"
cp -R "$raw_dir/." "$app_dir/Contents/MacOS/"
if [[ -d "$app_dir/Contents/MacOS/BundledKnowledge" ]]; then
  mv "$app_dir/Contents/MacOS/BundledKnowledge" "$app_dir/Contents/Resources/BundledKnowledge"
fi
# Move macOS bundle icon to Resources (published via Content copy)
if [[ -f "$app_dir/Contents/MacOS/AppIcon.icns" ]]; then
  mv "$app_dir/Contents/MacOS/AppIcon.icns" "$app_dir/Contents/Resources/AppIcon.icns"
elif [[ -f "$app_dir/Contents/MacOS/Assets/AppIcon.icns" ]]; then
  mv "$app_dir/Contents/MacOS/Assets/AppIcon.icns" "$app_dir/Contents/Resources/AppIcon.icns"
  rmdir "$app_dir/Contents/MacOS/Assets" 2>/dev/null || true
fi
if [[ ! -f "$app_dir/Contents/Resources/AppIcon.icns" ]]; then
  echo "Warning: AppIcon.icns not found in publish output; bundle will use default icon." >&2
fi
cp "$project_dir/ChatApp.UI/Platforms/macOS/Info.plist" "$app_dir/Contents/Info.plist"
chmod +x "$app_dir/Contents/MacOS/ChatApp.UI"
find "$app_dir/Contents/MacOS" -type f -name '*.pdb' -delete
# Sign each Mach-O file first and the completed bundle last. This keeps the
# resource seal consistent after Info.plist and native libraries are copied.
while IFS= read -r -d '' binary; do
  codesign --force --sign - "$binary"
done < <(find "$app_dir/Contents/MacOS" -type f \( -name '*.dylib' -o -name 'ChatApp.UI' \) -print0)
codesign --force --sign - "$app_dir"
codesign --verify --deep --strict "$app_dir"
xattr -cr "$app_dir" 2>/dev/null || true

knowledge_count="$(find "$app_dir/Contents/Resources/BundledKnowledge" -type f | wc -l | tr -d ' ')"
archive_path="$output_dir/ChatApp-macOS-arm64.zip"
ditto -c -k --sequesterRsrc --keepParent "$app_dir" "$archive_path"
archive_sha256="$(shasum -a 256 "$archive_path" | awk '{print $1}')"
echo "Created $app_dir with $knowledge_count bundled knowledge files"
echo "Created $archive_path"
echo "SHA-256: $archive_sha256"
