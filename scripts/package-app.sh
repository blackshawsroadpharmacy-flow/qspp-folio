#!/usr/bin/env bash
# Build the QSPP Folio macOS .app bundle (osx-arm64, self-contained).
# Output: <repo>/dist/QSPP Folio.app
#
# Run from the repo root:  scripts/package-app.sh
# Optional env:
#   QSPP_DATA_DIR — keep unset for normal users (defaults to
#   ~/Library/Application Support/QsppManager); set only for disposable test runs.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"
APP_NAME="QSPP Folio"
DIST="$ROOT/dist"
CONTENTS="$DIST/$APP_NAME.app/Contents"
PLIST_BUDDY="/usr/libexec/PlistBuddy"

echo "→ dotnet publish (osx-arm64, self-contained)"
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet publish src/QsppManager.Desktop/QsppManager.Desktop.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false -p:PublishTrimmed=false \
  -p:DebugType=none -p:DebugSymbols=false -p:PathMap="$ROOT=/q" -o "$DIST/payload" --nologo

echo "→ assembling bundle"
rm -rf "$DIST/$APP_NAME.app"
mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources"
cp -R "$DIST/payload/" "$CONTENTS/MacOS/"
rm -rf "$DIST/payload"
find "$CONTENTS/MacOS" -name "*.pdb" -delete
chmod +x "$CONTENTS/MacOS/QsppManager.Desktop"

cp assets/art/QsppManager.icns "$CONTENTS/Resources/"

# Bundle the verified corpus pack so the app finds its sources on any machine
# (discovery order: QSPP_PACK_ROOT → Settings override → vendored repo → bundled Resources).
PACK_SRC="$ROOT/vendor/qspp-pack"
[ -d "$PACK_SRC" ] || PACK_SRC="$(dirname "$ROOT")/vendor/qspp-pack"
if [ -d "$PACK_SRC" ]; then
  cp -R "$PACK_SRC" "$CONTENTS/Resources/qspp-pack"
else
  echo "  (no vendor pack found at $PACK_SRC — app will rely on Settings override)"
fi

# Bundle the seed corpus (451-requirement pack import + REM surveillance items).
# FindRepoRoot checks Contents/Resources/seed explicitly; without this the packaged
# app silently skips the 451-requirement import and the REM-01…REM-19 upsert.
if [ -d "$ROOT/seed" ]; then
  mkdir -p "$CONTENTS/Resources/seed"
  cp "$ROOT"/seed/*.json "$CONTENTS/Resources/seed/"
else
  echo "  (no seed corpus at $ROOT/seed — packaged app will import nothing)"
fi

# Bundle the local OCR worker (Apple Vision helper; tools/ocr/qspp-ocr.swift).
# Falls back to compiling it if build/qspp-ocr is missing. The helper is the app's
# ONLY OCR path — on-device, no network, no model downloads (see DD-37).
if [ ! -x "$ROOT/build/qspp-ocr" ]; then
  echo "→ compiling the Apple Vision OCR helper (tools/ocr/qspp-ocr.swift)"
  swiftc -O -o "$ROOT/build/qspp-ocr" "$ROOT/tools/ocr/qspp-ocr.swift"
fi
cp "$ROOT/build/qspp-ocr" "$CONTENTS/MacOS/qspp-ocr"
chmod +x "$CONTENTS/MacOS/qspp-ocr"

cat > "$CONTENTS/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>QSPP Folio</string>
    <key>CFBundleDisplayName</key><string>QSPP Folio</string>
    <key>CFBundleIdentifier</key><string>com.qsppfolio.app</string>
    <key>CFBundleVersion</key><string>1.0.0</string>
    <key>CFBundleShortVersionString</key><string>1.0.0</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleExecutable</key><string>QsppManager.Desktop</string>
    <key>CFBundleIconFile</key><string>QsppManager</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>LSArchitecturePriority</key>
    <array><string>arm64</string></array>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSHumanReadableCopyright</key><string>© 2026 QSPP Folio</string>
    <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
$PLIST_BUDDY -c "Print" "$CONTENTS/Info.plist" >/dev/null

echo "→ ad-hoc code signature"
codesign --force --deep --sign - "$DIST/$APP_NAME.app" 2>/dev/null || echo "(codesign skipped)"

echo "✓ built: $DIST/$APP_NAME.app"
echo "  launch with: open \"$DIST/$APP_NAME.app\""