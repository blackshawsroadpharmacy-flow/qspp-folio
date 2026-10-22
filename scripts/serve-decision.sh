#!/usr/bin/env bash
# Start the OPTIONAL stage-2 decision server for "Process and Sort" — entirely
# local, loopback 127.0.0.1:8642, pinned checkpoint (sha256-verified on first
# download by laya-apple; works offline afterwards). Stop with Ctrl+C or
# `pkill -f "laya-apple serve"`. The app works without it (deterministic stage
# 1 alone); when it is running the review queue also shows its refinement.
set -euo pipefail
cd "$(dirname "$0")/../tools/decision"
if [ ! -x ".venv/bin/laya-apple" ]; then
  echo "→ first run: creating the pinned environment (laya-apple==1.7.0, Python 3.12)"
  PATH="$HOME/.local/bin:$PATH" uv venv --python 3.12 .venv
  PATH="$HOME/.local/bin:$PATH" uv pip install --python .venv/bin/python "laya-apple[serve]==1.7.0"
fi
echo "→ stage-2 decision server on http://127.0.0.1:8642 (loopback only)"
exec .venv/bin/laya-apple serve --model laya-typed-decisions --port 8642 --log-level info