#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"

mkdir -p training/artifacts/qwen25-1.5b-strict-v2-lora
training/.venv/bin/python -m mlx_lm lora \
  --config training/configs/qwen25-1.5b-strict-v2-lora.yaml \
  --iters "${TRAIN_ITERS:-1000}"
