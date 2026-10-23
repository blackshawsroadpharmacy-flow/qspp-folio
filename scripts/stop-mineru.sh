#!/usr/bin/env bash
# Stop the local MinerU doclib server (the heavy OCR tier is start/stop per batch,
# never resident). Safe to run when it is already stopped.
set -euo pipefail
cd "$(dirname "$0")/../tools/ocr-heavy"
[ -x ".venv/bin/mineru" ] || { echo "MinerU is not installed — nothing to stop."; exit 0; }
.venv/bin/mineru server stop && echo "MinerU local server stopped."