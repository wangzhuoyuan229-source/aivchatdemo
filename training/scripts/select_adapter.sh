#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"

SOURCE_DIR="${SOURCE_ADAPTER_DIR:-training/artifacts/qwen25-1.5b-strict-lora}"
SOURCE_FILE="${SOURCE_ADAPTER_FILE:-0001000_adapters.safetensors}"
TARGET_DIR="${SELECTED_ADAPTER_DIR:-training/artifacts/qwen25-1.5b-selected}"

if [[ ! -f "$SOURCE_DIR/$SOURCE_FILE" || ! -f "$SOURCE_DIR/adapter_config.json" ]]; then
  echo "Missing adapter checkpoint or config in $SOURCE_DIR" >&2
  exit 1
fi

mkdir -p "$TARGET_DIR"
cp "$SOURCE_DIR/adapter_config.json" "$TARGET_DIR/adapter_config.json"
cp "$SOURCE_DIR/$SOURCE_FILE" "$TARGET_DIR/adapters.safetensors"
printf '%s\n' "$SOURCE_DIR/$SOURCE_FILE" > "$TARGET_DIR/SELECTED_FROM.txt"
echo "Selected adapter: $SOURCE_DIR/$SOURCE_FILE"
