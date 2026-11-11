#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GUI_OUT="${1:-$ROOT/dist/linux-x64}"
CLI_OUT="$ROOT/dist/cli-linux-x64"

# PS_VERSION comes from the release CI (a git tag with the leading "v" stripped).
# It is a global MSBuild property, so it overrides the <Version> fallback in
# Directory.Build.props and the SDK derives AssemblyVersion/FileVersion from it.
# Unset = local build, which stays at 0.0.0-dev and changes nothing else here.
VER_ARGS=()
if [ -n "${PS_VERSION:-}" ]; then
  VER_ARGS+=("-p:Version=${PS_VERSION}")
  echo "== version: ${PS_VERSION} =="
fi

echo "== publish GUI =="
rm -rf "$GUI_OUT"
dotnet publish "$ROOT/src/PsApp/PsApp.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false \
  "${VER_ARGS[@]}" \
  -o "$GUI_OUT"

echo "== publish CLI =="
rm -rf "$CLI_OUT"
dotnet publish "$ROOT/src/PsCli/PsCli.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false \
  "${VER_ARGS[@]}" \
  -o "$CLI_OUT"

mkdir -p "$ROOT/dist"
tar --mode=755 -czf "$ROOT/dist/poe2smoother-linux-x64.tar.gz" -C "$ROOT/dist" linux-x64
tar --mode=755 -czf "$ROOT/dist/pscli-linux-x64.tar.gz" -C "$ROOT/dist" cli-linux-x64

echo
echo "GUI: $GUI_OUT"
echo "CLI: $CLI_OUT"
echo "Tarballs in $ROOT/dist"