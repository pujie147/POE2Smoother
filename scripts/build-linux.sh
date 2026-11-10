#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GUI_OUT="${1:-$ROOT/dist/linux-x64}"
CLI_OUT="$ROOT/dist/cli-linux-x64"

echo "== publish GUI =="
rm -rf "$GUI_OUT"
dotnet publish "$ROOT/src/PsApp/PsApp.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false \
  -o "$GUI_OUT"

echo "== publish CLI =="
rm -rf "$CLI_OUT"
dotnet publish "$ROOT/src/PsCli/PsCli.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false \
  -o "$CLI_OUT"

mkdir -p "$ROOT/dist"
tar --mode=755 -czf "$ROOT/dist/poe2smoother-linux-x64.tar.gz" -C "$ROOT/dist" linux-x64
tar --mode=755 -czf "$ROOT/dist/pscli-linux-x64.tar.gz" -C "$ROOT/dist" cli-linux-x64

echo
echo "GUI: $GUI_OUT"
echo "CLI: $CLI_OUT"
echo "Tarballs in $ROOT/dist"