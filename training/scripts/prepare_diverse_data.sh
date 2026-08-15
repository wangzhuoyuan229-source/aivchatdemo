#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"

training/.venv/bin/python training/scripts/prepare_sft.py \
  --raw-dir training/data/raw \
  --output-dir training/data/strict-diverse \
  --eval-cases ChatApp.Tests/Evals/grounding-cases.json \
  --profile strict \
  --strict-recipe v2
