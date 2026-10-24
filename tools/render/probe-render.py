#!/usr/bin/env python3
"""Foreground probe: run the QSPP app with --page Requirements for N seconds, tee all
output to a file, then kill it. Prints whatever appeared."""
import os
import subprocess
import sys
import time
from pathlib import Path

REPO = Path("/Users/hughn/AI/qspp-folio")
DLL = REPO / "src/QsppManager.Desktop/bin/Debug/net10.0/QsppManager.Desktop.dll"

def main() -> int:
    secs = float(sys.argv[1]) if len(sys.argv) > 1 else 20.0
    env = dict(os.environ)
    env.update({
        "QSPP_DATA_DIR": os.path.expanduser("~/.hermes/cache/scratch/qspp-render"),
        "QSPP_DEMO": "1",
        "PATH": os.path.expanduser("~/.dotnet:") + env.get("PATH", ""),
        "DOTNET_ROOT": os.path.expanduser("~/.dotnet"),
        "DOTNET_gcServer": "0",
    })
    args = ["dotnet", str(DLL), "--render-offscreen", "/tmp/req4-probe.png",
            "--width", "1600", "--height", "1000", "--page", "Requirements",
            "--theme", "light"]
    log = open("/tmp/req4-probe.log", "w")
    proc = subprocess.Popen(args, cwd=str(REPO), env=env,
                            stdout=log, stderr=subprocess.STDOUT)
    deadline = time.time() + secs
    while time.time() < deadline and proc.poll() is None:
        time.sleep(0.5)
    alive = proc.poll() is None
    log.close()
    print("still_running" if alive else f"exited rc={proc.returncode}")
    print("---- last output lines ----")
    with open("/tmp/req4-probe.log") as f:
        print("".join(f.readlines()[-40:]) or "(no output at all)")
    proc.terminate()
    return 0

if __name__ == "__main__":
    sys.exit(main())