#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"

SOURCE_ADAPTER="${SOURCE_ADAPTER:-training/artifacts/qwen25-1.5b-strict-lora/0001000_adapters.safetensors}"
mkdir -p training/artifacts/qwen25-1.5b-dialogue-final
training/.venv/bin/python -m mlx_lm lora \
  --config training/configs/qwen25-1.5b-final-calibration.yaml \
  --resume-adapter-file "$SOURCE_ADAPTER" \
  --iters "${CALIBRATION_ITERS:-200}"
