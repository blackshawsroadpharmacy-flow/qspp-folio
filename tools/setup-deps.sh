#!/usr/bin/env bash
# Hugh's QSPP Manager development environment setup (user-local, no sudo).
set -uo pipefail
LOG=/home/vpnhugh/.hermes/cache/scratch/qspp-setup.log
echo "== setup start $(date -Is) ==" >> "$LOG"

# 1) .NET 10 SDK into ~/.dotnet
if ! ~/.dotnet/dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh >> "$LOG" 2>&1
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet" >> "$LOG" 2>&1
fi
"$HOME/.dotnet/dotnet" --list-sdks >> "$LOG" 2>&1
echo "-- sdk done" >> "$LOG"

# 2) Fonts (bundled with app; OFL/ISC licences)
F=/home/vpnhugh/Projects/qspp-manager/assets/fonts
mkdir -p "$F"
# JetBrains Mono statics (OFL)
if [ ! -f "$F/JetBrainsMono-Regular.ttf" ]; then
  curl -sSL https://github.com/JetBrains/JetBrainsMono/releases/download/v2.304/JetBrainsMono-2.304.zip -o /tmp/jbm.zip >> "$LOG" 2>&1
  unzip -o -q /tmp/jbm.zip -d /tmp/jbm >> "$LOG" 2>&1
  cp /tmp/jbm/fonts/ttf/JetBrainsMono-Regular.ttf "$F/" 2>/dev/null
  cp /tmp/jbm/fonts/ttf/JetBrainsMono-Medium.ttf "$F/" 2>/dev/null
  cp /tmp/jbm/fonts/ttf/JetBrainsMono-SemiBold.ttf "$F/" 2>/dev/null
  rm -rf /tmp/jbm /tmp/jbm.zip
fi
# Host Grotesk variable (OFL) from google/fonts; static instances if available
if [ ! -f "$F/HostGrotesk.ttf" ]; then
  curl -sSL "https://raw.githubusercontent.com/google/fonts/main/ofl/hostgrotesk/HostGrotesk%5Bwght%5D.ttf" -o "$F/HostGrotesk.ttf" >> "$LOG" 2>&1
  curl -sSL "https://raw.githubusercontent.com/google/fonts/main/ofl/hostgrotesk/HostGrotesk-Italic%5Bwght%5D.ttf" -o "$F/HostGrotesk-Italic.ttf" >> "$LOG" 2>&1 || true
fi
ls -la "$F" >> "$LOG"
echo "-- fonts done" >> "$LOG"

# 3) Lucide icon SVGs (ISC) — curated set
I=/home/vpnhugh/Projects/qspp-manager/assets/icons
mkdir -p "$I"
for name in house layout-dashboard list-checks clipboard-list calendar-days users \
            graduation-cap clipboard-check book-open printer settings search \
            file-text files folder-open boxes flask-conical truck shield-check \
            triangle-alert circle-check circle-alert clock history tag link \
            download upload external-link info x plus chevron-right chevron-down \
            arrow-right badge-check file-warning lock database save filter refresh-cw \
            id-card alert-triangle check check-circle-2 circle-dashed pen-line scroll-text \
            hand-coins scale user-users network; do
  curl -sS -f "https://unpkg.com/lucide-static@latest/icons/${name}.svg" -o "$I/${name}.svg" 2>/dev/null \
    || echo "MISSING_ICON:${name}" >> "$LOG"
done
ls "$I" | wc -l >> "$LOG"
echo "-- icons done" >> "$LOG"
echo "== setup complete $(date -Is) ==" >> "$LOG"