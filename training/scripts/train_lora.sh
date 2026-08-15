#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"

mkdir -p training/artifacts/qwen25-1.5b-dialogue-lora
training/.venv/bin/python -m mlx_lm lora \
  --config training/configs/qwen25-1.5b-lora.yaml \
  --iters "${TRAIN_ITERS:-800}"
