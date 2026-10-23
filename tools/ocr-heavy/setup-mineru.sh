#!/usr/bin/env bash
# Set up the pinned MinerU heavy-tier venv for evaluation + (optional) use.
# Pinned: mineru>=4.0,<5 on Python 3.12 (Apple Silicon recommended tier).
# Nothing here downloads models; model pulls are separate and user-approved.
set -euo pipefail
cd "$(dirname "$0")/../tools/ocr-heavy"
[ -x ".venv/bin/mineru" ] && { echo "venv ready"; exit 0; }
PATH="$HOME/.local/bin:$PATH" uv venv --python 3.12 .venv
PATH="$HOME/.local/bin:$PATH" uv pip install --python .venv/bin/python -U "mineru>=4.0,<5"
echo "installed:" && .venv/bin/mineru version 2>/dev/null || .venv/bin/mineru --version 2>/dev/null || true