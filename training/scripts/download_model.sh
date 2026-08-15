#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"

training/.venv/bin/python training/scripts/download_model.py \
  --output-dir training/cache/Qwen2.5-1.5B-Instruct \
  --parts "${MODEL_DOWNLOAD_PARTS:-8}"
