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

# --include-experimental → apply → restore, the whole loop on a blanking (.pet) rule.
# plan exits 6 when it finds nothing to change, which is exactly the unticked case.
echo "== CLI end-to-end: --include-experimental loop =="
EXP="$TMP/experimental"
EXP_BUNDLES="$EXP/Bundles2"
EXP_INDEX="$EXP_BUNDLES/_.index.bin"
EXP_PACK="$EXP/pack.json"
EXP_PATH="metadata/effects/spells/fire/burn.pet"
EXP_CONTENT="PARTICLE-DATA-DO-NOT-TOUCH"
EXPECTED=${#EXP_CONTENT}
# A blanked .pet is no longer 0 bytes: it is our minimal valid empty particle file
# (UTF-16LE BOM + "0"). Pinned here on purpose — if the template changes, this gate must
# be read and changed with it, not silently satisfied by "anything that is not zero".
PET_TEMPLATE=4

dotnet "$CLI" fixture --out "$EXP_BUNDLES" --path "$EXP_PATH" --content "$EXP_CONTENT" >/dev/null
dotnet "$CLI" build-rules --index "$EXP_INDEX" --out "$EXP_PACK" --name "Exp Test" --build synthetic >/dev/null

exp_size() {
  dotnet "$CLI" read-file --index "$EXP_INDEX" --path "$EXP_PATH" | grep "^size" | tr -dc '0-9'
}

PLAN=$(dotnet "$CLI" plan --index "$EXP_INDEX" --pack "$EXP_PACK" --preset safe) || true
echo "$PLAN" | grep -q "files      : 0" || { echo "$PLAN"; exit 1; }
echo "$PLAN" | grep -q "skippedExp : 1" || { echo "$PLAN"; exit 1; }

APPLY=$(dotnet "$CLI" apply --index "$EXP_INDEX" --pack "$EXP_PACK" --preset safe --yes)
echo "$APPLY" | grep -q "files      : 0" || { echo "$APPLY"; exit 1; }
echo "$APPLY" | grep -q "skippedExp : 1" || { echo "$APPLY"; exit 1; }
dotnet "$CLI" status --index "$EXP_INDEX" | grep -q "modified      : False" || { echo "unticked apply must not modify the client"; exit 1; }
[ "$(exp_size)" = "$EXPECTED" ] || { echo "unticked apply touched $EXP_PATH"; exit 1; }

APPLY=$(dotnet "$CLI" apply --index "$EXP_INDEX" --pack "$EXP_PACK" --preset safe --include-experimental --yes)
echo "$APPLY" | grep -q "files      : 1" || { echo "$APPLY"; exit 1; }
echo "$APPLY" | grep -q "skippedExp : 0" || { echo "$APPLY"; exit 1; }
dotnet "$CLI" status --index "$EXP_INDEX" | grep -q "modified      : True" || { echo "ticked apply must report modified"; exit 1; }
[ "$(exp_size)" = "$PET_TEMPLATE" ] || { echo "--include-experimental did not write the .pet template ($(exp_size) bytes)"; exit 1; }

RESTORE=$(dotnet "$CLI" restore --index "$EXP_INDEX" --yes)
echo "$RESTORE" | grep -q "index restored" || { echo "$RESTORE"; exit 1; }
[ "$(exp_size)" = "$EXPECTED" ] || { echo "restore did not give $EXP_PATH back"; exit 1; }
dotnet "$CLI" status --index "$EXP_INDEX" | grep -q "modified      : False" || { echo "restore must return to original"; exit 1; }

# Risk tiers on the CLI: the value option must parse, must validate, and --include-experimental
# must still reach the top tier — scripts written against the old boolean cannot silently narrow.
echo "== CLI end-to-end: risk tiers =="
TIERS="$TMP/tiers"
TIERS_BUNDLES="$TIERS/Bundles2"
TIERS_INDEX="$TIERS_BUNDLES/_.index.bin"
TIERS_PACK="$TIERS/pack.json"
TIERS_PET="metadata/effects/spells/fire/spark.pet"

dotnet "$CLI" fixture --out "$TIERS_BUNDLES" --path "$TIERS_PET" --content "PARTICLE-BYTES" >/dev/null
dotnet "$CLI" build-rules --index "$TIERS_INDEX" --out "$TIERS_PACK" --name "Tier Test" --build synthetic >/dev/null

tiers_size() {
  dotnet "$CLI" read-file --index "$TIERS_INDEX" --path "$1" | grep "^size" | tr -dc '0-9'
}

fail() { echo "$1"; shift; printf '%s\n' "$@"; exit 1; }

# safe (and its "off" alias) never blanks.
dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental off --yes >/dev/null
if [ "$(tiers_size "$TIERS_PET")" = "0" ]; then fail "--experimental off blanked $TIERS_PET"; fi

# fx blanks the particle file.
APPLY=$(dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --yes)
if ! grep -q "files      : 1" <<<"$APPLY"; then fail "--experimental fx should blank the particle rule" "$APPLY"; fi
if [ "$(tiers_size "$TIERS_PET")" != "$PET_TEMPLATE" ]; then fail "--experimental fx did not write the template: $(tiers_size "$TIERS_PET") bytes"; fi

# a bogus tier is an error, not a silent downgrade to safe.
if dotnet "$CLI" plan --index "$TIERS_INDEX" --pack "$TIERS_PACK" --experimental everything >/dev/null 2>&1; then
  fail "--experimental accepted a bogus tier"
fi

# the legacy flag still means "as far as it ever went: material".
dotnet "$CLI" restore --index "$TIERS_INDEX" --yes >/dev/null
APPLY=$(dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --include-experimental --yes)
if ! grep -q "files      : 1" <<<"$APPLY"; then fail "--include-experimental stopped meaning material" "$APPLY"; fi
if [ "$(tiers_size "$TIERS_PET")" != "$PET_TEMPLATE" ]; then fail "--include-experimental did not write the template"; fi
dotnet "$CLI" restore --index "$TIERS_INDEX" --yes >/dev/null

# Scoped restore. This fixture has a single fx entry, so:
#  - --undo-tiers material leaves it suppressed (a real subset: per-file, kept=1, file stays blank);
#  - --undo-tiers fx covers every entry, i.e. the same request as an unscoped restore, which takes
#    the byte-identical whole-index path. Assert outcomes, not which branch printed them.
dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --yes >/dev/null
UNDO=$(dotnet "$CLI" restore --index "$TIERS_INDEX" --undo-tiers material --yes)
if ! grep -q "kept=1" <<<"$UNDO"; then fail "--undo-tiers material should keep the fx suppression" "$UNDO"; fi
if [ "$(tiers_size "$TIERS_PET")" != "$PET_TEMPLATE" ]; then fail "--undo-tiers material undid the fx blanking"; fi

UNDO=$(dotnet "$CLI" restore --index "$TIERS_INDEX" --undo-tiers fx --yes)
if [ "$(tiers_size "$TIERS_PET")" = "$PET_TEMPLATE" ]; then fail "--undo-tiers fx did not give $TIERS_PET back" "$UNDO"; fi
if [ -f "$POE2SMOOTHER_HOME/state.json" ]; then
  fail "a restore that undoes every entry must clear the state file"
fi

# The 0-byte fallback has to still do what it says, on the same fixture.
dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --blank-style empty --yes >/dev/null
if [ "$(tiers_size "$TIERS_PET")" != "0" ]; then
  fail "--blank-style empty did not write a 0-byte file ($(tiers_size "$TIERS_PET") bytes)"
fi
if ! BLANK_ERR=$(dotnet "$CLI" plan --index "$TIERS_INDEX" --pack "$TIERS_PACK" --experimental fx --blank-style sideways 2>&1); then
  # Assert the reason: any failure would otherwise pass, including "no such record".
  echo "$BLANK_ERR" | grep -qi "unknown --blank-style" || fail "--blank-style rejected for the wrong reason" "$BLANK_ERR"
else
  fail "--blank-style accepted a bogus value"
fi
dotnet "$CLI" restore --index "$TIERS_INDEX" --yes >/dev/null

# Write strategies on the same fixture. The .pet is not a compression candidate, so it shows the
# blanking-off vs blanking-on split: compress-only never blanks it, compress-then-* still do.
PET_ORIG=$(tiers_size "$TIERS_PET")
dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --blank-style compress-only --yes >/dev/null
if [ "$(tiers_size "$TIERS_PET")" != "$PET_ORIG" ]; then
  fail "--blank-style compress-only blanked a non-candidate .pet ($(tiers_size "$TIERS_PET") bytes)"
fi
# No restore here: compress-only wrote 0 files, so there is no applied state to undo (restore
# would throw "No backup path is known", exactly like the --experimental off case above).

dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --blank-style compress-then-template --yes >/dev/null
if [ "$(tiers_size "$TIERS_PET")" != "$PET_TEMPLATE" ]; then
  fail "--blank-style compress-then-template did not blank the .pet to a template"
fi
dotnet "$CLI" restore --index "$TIERS_INDEX" --yes >/dev/null

dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --blank-style compress-then-empty --yes >/dev/null
if [ "$(tiers_size "$TIERS_PET")" != "0" ]; then
  fail "--blank-style compress-then-empty did not write a 0-byte .pet"
fi
dotnet "$CLI" restore --index "$TIERS_INDEX" --yes >/dev/null

# Class flags without an explicit strategy auto-promote to compress-then-template (old-script compat)
# and say so; the .pet still blanks because it is not a compression candidate.
PROMO=$(dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --compress-shader --yes 2>&1)
if [ "$(tiers_size "$TIERS_PET")" != "$PET_TEMPLATE" ]; then
  fail "compression class flags without --blank-style should promote to compress-then-template and blank the .pet"
fi
echo "$PROMO" | grep -qi "compress-then-template" || fail "promotion should note the chosen strategy" "$PROMO"
dotnet "$CLI" restore --index "$TIERS_INDEX" --yes >/dev/null

# health: the client log is the only party that knows whether a blanked asset actually loaded.
# Plant a complaint naming the file we blanked; health must find exactly it and say so loudly.
dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --yes >/dev/null
mkdir -p "$TIERS/logs"
printf '%s\n' '2026-01-01 00:00:00 000000 abcdef [WARN Client 1] Failed to create effect graph node "X" in graph "Metadata/Effects/Spells/Fire/Spark.PET"' > "$TIERS/logs/LatestClient.txt"
printf '%s\n' '2026-01-01 00:00:01 000000 abcdef [WARN Client 1] Some unrelated warning about "Metadata/Art/Other/Untouched.mat"' >> "$TIERS/logs/LatestClient.txt"
if HEALTH=$(dotnet "$CLI" health --index "$TIERS_INDEX"); then health_code=0; else health_code=$?; fi
if [ "$health_code" != "7" ]; then echo "health should flag the planted complaint (exit $health_code)"; echo "$HEALTH"; exit 1; fi
echo "$HEALTH" | grep -q "suspect    : 1" || { echo "health should find exactly one suspect"; echo "$HEALTH"; exit 1; }
echo "$HEALTH" | grep -q "$TIERS_PET" || { echo "health should name the blanked file"; echo "$HEALTH"; exit 1; }
if echo "$HEALTH" | grep -q "Untouched.mat"; then echo "health must not blame files this tool never wrote"; echo "$HEALTH"; exit 1; fi
rm -rf "$TIERS/logs"
dotnet "$CLI" restore --index "$TIERS_INDEX" --yes >/dev/null

# Whole-index options cannot honour a subset; combining them has to fail, not silently overwrite.
# Re-apply first: with no state at all the command would also fail, and the test would pass for the
# wrong reason.
dotnet "$CLI" apply --index "$TIERS_INDEX" --pack "$TIERS_PACK" --preset safe --experimental fx --yes >/dev/null
if dotnet "$CLI" restore --index "$TIERS_INDEX" --undo-tiers fx --force --yes >/dev/null 2>&1; then
  fail "--undo-tiers accepted --force"
fi
# Check the reason each rejection gives: without an applied state every one of these would also
# fail, so a bare nonzero exit proves nothing.
if ! FORCE_ERR=$(dotnet "$CLI" restore --index "$TIERS_INDEX" --undo-tiers fx --force --yes 2>&1); then
  echo "$FORCE_ERR" | grep -qi "drop --force" || fail "--undo-tiers + --force refused for the wrong reason" "$FORCE_ERR"
fi
if ! TIER_ERR=$(dotnet "$CLI" restore --index "$TIERS_INDEX" --undo-tiers everything --yes 2>&1); then
  echo "$TIER_ERR" | grep -qi "unknown --undo-tiers" || fail "--undo-tiers rejected for the wrong reason" "$TIER_ERR"
fi
dotnet "$CLI" restore --index "$TIERS_INDEX" --yes >/dev/null

echo
echo "ALL TESTS PASSED"