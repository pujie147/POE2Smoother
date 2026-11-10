#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH="$ROOT/dist/linux-x64"
BUILD="$ROOT/build"
APPDIR="$BUILD/PoE2Smoother.AppDir"
SKIP_PUBLISH=0
if [ "${1:-}" = "--no-publish" ]; then
  SKIP_PUBLISH=1
fi

if [ "$(uname -s)" != "Linux" ]; then
  echo "AppImage must be built on Linux (SteamOS/Ubuntu) or inside WSL."
  echo "From Windows:"
  echo "  1) bash scripts/build-linux.sh"
  echo "  2) wsl -d Ubuntu -- bash /mnt/c/.../scripts/build-appimage.sh --no-publish"
  exit 1
fi

if [ "$SKIP_PUBLISH" -eq 0 ]; then
  # bash, not exec: build-linux.sh is tracked as 0644, so a fresh clone has no
  # execute bit on it and a direct invocation fails with "permission denied"
  bash "$ROOT/scripts/build-linux.sh" "$PUBLISH"
elif [ ! -d "$PUBLISH" ]; then
  echo "Missing $PUBLISH. Run scripts/build-linux.sh first, or omit --no-publish."
  exit 1
fi

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/icons/hicolor/scalable/apps"
mkdir -p "$APPDIR/usr/share/applications"

cp -a "$PUBLISH/." "$APPDIR/usr/bin/"
mkdir -p "$APPDIR/usr/lib"
if [ -d "$ROOT/packaging/extra-libs" ]; then
  cp -a "$ROOT/packaging/extra-libs/." "$APPDIR/usr/lib/"
fi
cp "$ROOT/packaging/poe2smoother.svg" "$APPDIR/usr/share/icons/hicolor/scalable/apps/poe2smoother.svg"
cp "$ROOT/packaging/poe2smoother.svg" "$APPDIR/poe2smoother.svg"
cp "$ROOT/packaging/poe2smoother.desktop" "$APPDIR/poe2smoother.desktop"
cp "$ROOT/packaging/poe2smoother.desktop" "$APPDIR/usr/share/applications/poe2smoother.desktop"

# appimagetool creates .DirIcon from a top-level poe2smoother.png. It only warns
# when the icon is missing today, but says it will refuse to pack in a future
# release, so render one from the SVG whenever rsvg-convert is available.
if command -v rsvg-convert >/dev/null 2>&1; then
  rsvg-convert -w 256 -h 256 "$ROOT/packaging/poe2smoother.svg" -o "$APPDIR/poe2smoother.png"
fi

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="$HERE/usr/lib:$HERE/usr/bin${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec "$HERE/usr/bin/poe2smoother" "$@"
EOF
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/poe2smoother"

# appimagetool (build 370+) refuses AppDirs whose files are not world-readable,
# and the umask here turns git's 0755 modes into 0744. Normalise: directories and
# the two entry points executable for everyone, everything else plain data.
find "$APPDIR" -type d -exec chmod 0755 {} +
find "$APPDIR" -type f -exec chmod 0644 {} +
chmod 0755 "$APPDIR/AppRun" "$APPDIR/usr/bin/poe2smoother"

APPIMAGETOOL_BIN="$(command -v appimagetool || true)"
APPIMAGETOOL_ARGS=""
if [ -z "$APPIMAGETOOL_BIN" ] && [ -f "$ROOT/vendor/appimage/appimagetool-x86_64.AppImage" ]; then
  APPIMAGETOOL_BIN="$ROOT/vendor/appimage/appimagetool-x86_64.AppImage"
  APPIMAGETOOL_ARGS="--appimage-extract-and-run"
fi

if [ -n "$APPIMAGETOOL_BIN" ]; then
  echo "== building AppImage =="
  chmod +x "$APPIMAGETOOL_BIN" 2>/dev/null || true
  TOOL_ARGS=()
  if [ -n "$APPIMAGETOOL_ARGS" ]; then
    TOOL_ARGS+=("$APPIMAGETOOL_ARGS")
  fi
  TOOL_ARGS+=(--no-appstream)
  RUNTIME_FILE="$ROOT/vendor/appimage/runtime-x86_64"
  if [ -f "$RUNTIME_FILE" ]; then
    TOOL_ARGS+=(--runtime-file "$RUNTIME_FILE")
  fi
  ARCH=x86_64 "$APPIMAGETOOL_BIN" "${TOOL_ARGS[@]}" \
    "$APPDIR" "$ROOT/dist/PoE2Smoother-x86_64.AppImage"
  echo "AppImage: $ROOT/dist/PoE2Smoother-x86_64.AppImage"
else
  echo "appimagetool not found."
  echo "Download it into vendor/appimage/appimagetool-x86_64.AppImage, or install it."
  echo "Then run:"
  echo "  ARCH=x86_64 appimagetool --no-appstream \"$APPDIR\" \"$ROOT/dist/PoE2Smoother-x86_64.AppImage\""
  tar --mode=755 -czf "$ROOT/dist/PoE2Smoother-portable-linux-x64.tar.gz" -C "$BUILD" PoE2Smoother.AppDir
  echo "Portable AppDir tarball: $ROOT/dist/PoE2Smoother-portable-linux-x64.tar.gz"
fi