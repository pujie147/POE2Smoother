#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet build "$ROOT/PoE2Smoother.sln" -c Release
dotnet "$ROOT/tests/PsTests/bin/Release/net8.0/pstests.dll"
echo "CORE TESTS PASSED"
