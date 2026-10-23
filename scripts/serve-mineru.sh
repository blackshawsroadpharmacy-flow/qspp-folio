#!/usr/bin/env bash
# Start the OPTIONAL heavy-OCR server (MinerU 4 doclib) for "Process and Sort" —
# entirely local, loopback 127.0.0.1 only (verified default), pinned venv.
# The heavy tier is opt-in per batch; with the server OFF the app falls back to
# Apple Vision every time. Stop with scripts/stop-mineru.sh.
set -euo pipefail
cd "$(dirname "$0")/../tools/ocr-heavy"

if [ ! -x ".venv/bin/mineru" ]; then
  echo "MinerU is not installed yet — run tools/ocr-heavy/setup-mineru.sh first." >&2
  echo "Then the model download (~2 GB, user-approved one-time task) with:" >&2
  echo "  .venv/bin/mineru-models-download --tier standard" >&2
  exit 1
fi

V=".venv/bin/mineru"
# local-only posture, idempotent: telemetry off + managed parse mode
"$V" telemetry disable >/dev/null 2>&1 || true
"$V" config set parse_server.local.mode managed >/dev/null 2>&1 || true

if "$V" server status >/dev/null 2>&1; then
  echo "MinerU local server already running (loopback). Heavy tier is live in the app."
else
  "$V" server start
  echo "MinerU local server started — loopback only, nothing off-device."
  echo "Select 'MinerU heavy' in the Process and Sort dialog for a batch."
fi