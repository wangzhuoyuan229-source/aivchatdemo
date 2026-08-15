#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PYTHON_BIN="${PYTHON_BIN:-python3}"
INDEX_URL="${CHATAPP_PIP_INDEX_URL:-https://pypi.tuna.tsinghua.edu.cn/simple}"

cd "$ROOT"
"$PYTHON_BIN" -c 'import sys; assert sys.version_info >= (3, 10), "MLX LM requires Python 3.10+"'
"$PYTHON_BIN" -m venv training/.venv
training/.venv/bin/python -m pip install --upgrade pip
training/.venv/bin/pip install -i "$INDEX_URL" -r training/requirements-mlx.txt
