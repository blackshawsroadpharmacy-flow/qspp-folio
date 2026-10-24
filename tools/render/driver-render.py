#!/usr/bin/env python3
"""Phase-4 offscreen render driver (revives the phase-3 procedure).

Launches the built QSPP app offscreen against the scratch data dir, waits for the
"Shell offscreen render" window, captures it with the OS compositor, and kills the
process. One page per invocation:
    python3 driver-render.py <page> <light|dark> <out.png> [width] [height]
"""
import os
import subprocess
import sys
import time
from pathlib import Path

REPO = Path("/Users/hughn/AI/qspp-folio")
APP = REPO / "src/QsppManager.Desktop/bin/Debug/net10.0/QsppManager.Desktop"
WINLIST = "/tmp/winlist"
TITLE = "Shell offscreen render"


def main() -> int:
    page, theme, out = sys.argv[1], sys.argv[2], sys.argv[3]
    width = sys.argv[4] if len(sys.argv) > 4 else "1920"
    height = sys.argv[5] if len(sys.argv) > 5 else "1200"
    seg = sys.argv[6] if len(sys.argv) > 6 else ""
    env = dict(os.environ)
    env.update({
        "QSPP_DATA_DIR": os.path.expanduser("~/.hermes/cache/scratch/qspp-render"),
        # QSPP_DEMO is deliberately NOT set: demo mode reads qspp-demo.db, which carries the
        # imported corpus but no owner states, so every row rendered "No owner yet" while the
        # real workspace showed "Owner: Hugh Nguyen". The populated scratch qspp.db mirrors
        # production (all 470 owned by Hugh Nguyen); renders must show that truth (owner item 9),
        "PATH": os.path.expanduser("~/.dotnet:") + env.get("PATH", ""),
        "DOTNET_ROOT": os.path.expanduser("~/.dotnet"),
    })
    launch = [str(APP), "--render-offscreen", out, "--width", width,
              "--height", height, "--page", page, "--theme", theme, "--hold"]
    if seg:
        launch += ["--seg", seg]
    proc = subprocess.Popen(
        launch, cwd=str(REPO), env=env,
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT,
    )
    win_id = None
    deadline = time.time() + 90
    while time.time() < deadline and win_id is None:
        time.sleep(0.5)
        if proc.poll() is not None:
            print(f"APP EXITED EARLY rc={proc.returncode} for {page}/{theme}")
            return 1
        try:
            listing = subprocess.run([WINLIST], capture_output=True, text=True, timeout=10).stdout
        except Exception:
            continue
        for line in listing.splitlines():
            if TITLE in line:
                win_id = line.split("|", 1)[0].strip()
                break
    if win_id is None:
        print(f"WINDOW NEVER APPEARED for {page}/{theme}")
        proc.terminate()
        return 1
    # Settle for composition, then capture.
    time.sleep(1.4)
    cap = subprocess.run(
        ["/usr/sbin/screencapture", "-x", "-o", "-l", str(win_id), out],
        capture_output=True, text=True)
    proc.terminate()
    try:
        proc.wait(timeout=8)
    except subprocess.TimeoutExpired:
        proc.kill()
    ok = Path(out).exists() and cap.returncode == 0
    print(("OK " if ok else "CAPTURE FAILED ") + f"{page}/{theme} win={win_id} -> {out}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())