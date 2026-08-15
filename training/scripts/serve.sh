#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"

training/.venv/bin/python -m mlx_lm server \
  --model training/cache/Qwen2.5-1.5B-Instruct \
  --adapter-path "${CHATAPP_ADAPTER_PATH:-training/artifacts/qwen25-1.5b-selected}" \
  --host 127.0.0.1 \
  --port "${CHATAPP_MODEL_PORT:-8080}" \
  --temp 0.65 \
  --top-p 1.0 \
  --max-tokens 512
