#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP="$ROOT/.test-all"
CLI="$ROOT/src/PsCli/bin/Release/net8.0/pscli.dll"
TESTS="$ROOT/tests/PsTests/bin/Release/net8.0/pstests.dll"

echo "== build =="
dotnet build "$ROOT/PoE2Smoother.sln" -c Release

# Cross-publish must pick the matching vendored natives: a win-x64 publish built on a Linux
# host used to fall through to the host-OS condition and ship the linux-x64 DLLs.
echo "== vendor native dir (RID selection) =="
for rid in win-x64 linux-x64; do
  dir="$(dotnet msbuild "$ROOT/src/PsCore/PsCore.csproj" -getProperty:LibGGPK3Dir -p:RuntimeIdentifier="$rid" -nologo | tr -d '\r')"
  if [ "${dir##*/}" != "$rid" ]; then
    echo "RID selection broken: RuntimeIdentifier=$rid resolved to '$dir'"
    exit 1
  fi
done

echo "== unit/integration tests =="
dotnet "$TESTS"

echo "== GUI headless smoke test =="
dotnet "$ROOT/tests/PsAppTests/bin/Release/net8.0/psapptests.dll"

echo "== CLI end-to-end =="
rm -rf "$TMP"
mkdir -p "$TMP"
export POE2SMOOTHER_HOME="$TMP/home"

BUNDLES="$TMP/Bundles2"
INDEX="$BUNDLES/_.index.bin"
PACK="$TMP/pack.json"
CONTENT=$'version 1\nAddAttached( \'metadata/effects/spells/fire/burn.pet\', <root> );\nAddEffectPack( \'metadata/effects/spells/fire/burn.epk\' );\nKeepThis();\n'

dotnet "$CLI" fixture --out "$BUNDLES" --path "metadata/effects/spells/fire/fireball.ao" --content "$CONTENT" >/dev/null

BUILD=$(dotnet "$CLI" build-rules --index "$INDEX" --out "$PACK" --name "CLI Test" --build "synthetic")
echo "$BUILD" | grep -q "rules      : 1" || { echo "$BUILD"; exit 1; }

PLAN=$(dotnet "$CLI" plan --index "$INDEX" --pack "$PACK" --preset safe)
echo "$PLAN" | grep -q "files      : 1" || { echo "$PLAN"; exit 1; }

APPLY=$(dotnet "$CLI" apply --index "$INDEX" --pack "$PACK" --preset safe --yes)
echo "$APPLY" | grep -q "Apply complete" || { echo "$APPLY"; exit 1; }

STATUS=$(dotnet "$CLI" status --index "$INDEX")
echo "$STATUS" | grep -q "modified      : True" || { echo "$STATUS"; exit 1; }

AFTER=$(dotnet "$CLI" read-file --index "$INDEX" --path "metadata/effects/spells/fire/fireball.ao" --text)
if grep -q "AddAttached" <<<"$AFTER"; then echo "$AFTER"; exit 1; fi
grep -q "KeepThis" <<<"$AFTER" || { echo "$AFTER"; exit 1; }

RESTORE=$(dotnet "$CLI" restore --index "$INDEX" --yes)
echo "$RESTORE" | grep -q "index restored" || { echo "$RESTORE"; exit 1; }

ORIGINAL=$(dotnet "$CLI" read-file --index "$INDEX" --path "metadata/effects/spells/fire/fireball.ao" --text)
grep -q "AddAttached" <<<"$ORIGINAL" || { echo "$ORIGINAL"; exit 1; }

dotnet "$CLI" profiles --action save --name test --preset safe --categories skill.fire >/dev/null
dotnet "$CLI" profiles --action list | grep -q "test"
dotnet "$CLI" profiles --action delete --name test >/dev/null

echo
echo "ALL TESTS PASSED"