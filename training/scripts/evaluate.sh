#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
MODEL="training/cache/Qwen2.5-1.5B-Instruct"
ADAPTER="training/artifacts/qwen25-1.5b-selected"
CASES="ChatApp.Tests/Evals/grounding-cases.json"
cd "$ROOT"

mkdir -p training/reports/generated
training/.venv/bin/python training/scripts/evaluate_model.py \
  --model "$MODEL" --cases "$CASES" --repeats 3 \
  --output training/reports/generated/baseline.json

if [[ -f "$ADAPTER/adapters.safetensors" ]]; then
  training/.venv/bin/python training/scripts/evaluate_model.py \
    --model "$MODEL" --adapter-path "$ADAPTER" --cases "$CASES" --repeats 3 \
    --output training/reports/generated/selected.json
fi
