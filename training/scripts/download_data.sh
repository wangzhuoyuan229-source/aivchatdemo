#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"

# Passing this flag confirms the non-commercial NaturalConv LICENSE was reviewed.
training/.venv/bin/python training/scripts/download_datasets.py \
  --raw-dir training/data/raw \
  --lccc-records "${LCCC_RECORDS:-20000}" \
  --accept-naturalconv-license
