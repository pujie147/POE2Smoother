$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Tmp = Join-Path $Root ".test-all"
$Cli = Join-Path $Root "src/PsCli/bin/Release/net8.0/pscli.dll"
$Tests = Join-Path $Root "tests/PsTests/bin/Release/net8.0/pstests.dll"

Write-Host "== build =="
dotnet build (Join-Path $Root "PoE2Smoother.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }

# Cross-publish must pick the matching vendored natives: a win-x64 publish built on a Linux
# host used to fall through to the host-OS condition and ship the linux-x64 DLLs.
Write-Host "== vendor native dir (RID selection) =="
foreach ($rid in @("win-x64", "linux-x64")) {
    $dir = (dotnet msbuild (Join-Path $Root "src/PsCore/PsCore.csproj") -getProperty:LibGGPK3Dir -p:RuntimeIdentifier=$rid -nologo) -join ""
    if ((Split-Path -Leaf $dir.Trim()) -ne $rid) {
        throw "RID selection broken: RuntimeIdentifier=$rid resolved to '$dir'"
    }
}

Write-Host "== unit/integration tests =="
dotnet $Tests
if ($LASTEXITCODE -ne 0) { throw "test suite failed" }

Write-Host "== GUI headless smoke test =="
dotnet (Join-Path $Root "tests/PsAppTests/bin/Release/net8.0/psapptests.dll")
if ($LASTEXITCODE -ne 0) { throw "GUI smoke test failed" }

Write-Host "== CLI end-to-end =="
Remove-Item -Recurse -Force $Tmp -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $Tmp | Out-Null
$env:POE2SMOOTHER_HOME = Join-Path $Tmp "home"

$bundles = Join-Path $Tmp "Bundles2"
$index = Join-Path $bundles "_.index.bin"
$pack = Join-Path $Tmp "pack.json"
$content = @"
version 1
AddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );
AddEffectPack( 'metadata/effects/spells/fire/burn.epk' );
KeepThis();
"@

dotnet $Cli fixture --out $bundles --path "metadata/effects/spells/fire/fireball.ao" --content $content | Out-Null
if ($LASTEXITCODE -ne 0) { throw "fixture failed" }

$build = (dotnet $Cli build-rules --index $index --out $pack --name "CLI Test" --build "synthetic") -join "`n"
if ($LASTEXITCODE -ne 0 -or $build -notmatch "rules      : 1") { throw "build-rules failed`n$build" }

$plan = (dotnet $Cli plan --index $index --pack $pack --preset safe) -join "`n"
if ($LASTEXITCODE -ne 0 -or $plan -notmatch "files      : 1") { throw "plan failed`n$plan" }

$apply = (dotnet $Cli apply --index $index --pack $pack --preset safe --yes) -join "`n"
if ($LASTEXITCODE -ne 0 -or $apply -notmatch "Apply complete") { throw "apply failed`n$apply" }

$status = (dotnet $Cli status --index $index) -join "`n"
if ($status -notmatch "modified      : True") { throw "status did not report modified`n$status" }

$after = (dotnet $Cli read-file --index $index --path "metadata/effects/spells/fire/fireball.ao" --text) -join "`n"
if ($after -match "AddAttached") { throw "AddAttached was not removed`n$after" }
if ($after -notmatch "KeepThis") { throw "unrelated statement was lost`n$after" }

$restore = (dotnet $Cli restore --index $index --yes) -join "`n"
if ($LASTEXITCODE -ne 0 -or $restore -notmatch "index restored") { throw "restore failed`n$restore" }

$original = (dotnet $Cli read-file --index $index --path "metadata/effects/spells/fire/fireball.ao" --text) -join "`n"
if ($original -notmatch "AddAttached") { throw "restore did not bring back the original content`n$original" }

$profile = (dotnet $Cli profiles --action save --name test --preset safe --categories skill.fire) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "profile save failed`n$profile" }
$profiles = (dotnet $Cli profiles --action list) -join "`n"
if ($profiles -notmatch "test") { throw "profile list failed`n$profiles" }
dotnet $Cli profiles --action delete --name test | Out-Null

# --include-experimental → apply → restore, the whole loop on a blanking (.pet) rule.
# plan exits 6 when it finds nothing to change, which is exactly the unticked case.
Write-Host "== CLI end-to-end: --include-experimental loop =="
$exp = Join-Path $Tmp "experimental"
$expBundles = Join-Path $exp "Bundles2"
$expIndex = Join-Path $expBundles "_.index.bin"
$expPack = Join-Path $exp "pack.json"
$expPath = "metadata/effects/spells/fire/burn.pet"
$expContent = "PARTICLE-DATA-DO-NOT-TOUCH"
$expected = $expContent.Length
# A blanked .pet is our minimal valid empty particle file (UTF-16LE BOM + "0"), not 0 bytes.
# Pinned: a stub-size change must break this gate loudly rather than be satisfied by "not zero".
$petTemplate = 4

function Exp-Size {
    $read = (dotnet $Cli read-file --index $expIndex --path $expPath) -join "`n"
    if ($read -match "size\s*:\s*(\d+)") { return [int]$Matches[1] }
    throw "read-file did not report a size`n$read"
}

dotnet $Cli fixture --out $expBundles --path $expPath --content $expContent | Out-Null
if ($LASTEXITCODE -ne 0) { throw "experimental fixture failed" }
dotnet $Cli build-rules --index $expIndex --out $expPack --name "Exp Test" --build synthetic | Out-Null
if ($LASTEXITCODE -ne 0) { throw "experimental build-rules failed" }

$plan = (dotnet $Cli plan --index $expIndex --pack $expPack --preset safe) -join "`n"
if ($plan -notmatch "files      : 0") { throw "unticked plan should change nothing`n$plan" }
if ($plan -notmatch "skippedExp : 1") { throw "unticked plan should report the skipped rule`n$plan" }

$apply = (dotnet $Cli apply --index $expIndex --pack $expPack --preset safe --yes) -join "`n"
if ($apply -notmatch "files      : 0" -or $apply -notmatch "skippedExp : 1") { throw "unticked apply must skip the blanking rule`n$apply" }
$status = (dotnet $Cli status --index $expIndex) -join "`n"
if ($status -notmatch "modified      : False") { throw "unticked apply must not modify the client`n$status" }
if ((Exp-Size) -ne $expected) { throw "unticked apply touched $expPath" }

$apply = (dotnet $Cli apply --index $expIndex --pack $expPack --preset safe --include-experimental --yes) -join "`n"
if ($apply -notmatch "files      : 1" -or $apply -notmatch "skippedExp : 0") { throw "--include-experimental must blank the rule`n$apply" }
$status = (dotnet $Cli status --index $expIndex) -join "`n"
if ($status -notmatch "modified      : True") { throw "ticked apply must report modified`n$status" }
if ((Exp-Size) -ne $petTemplate) { throw "--include-experimental did not write the .pet template ($(Exp-Size) bytes)" }

$restore = (dotnet $Cli restore --index $expIndex --yes) -join "`n"
if ($restore -notmatch "index restored") { throw "restore failed`n$restore" }
if ((Exp-Size) -ne $expected) { throw "restore did not give $expPath back" }
$status = (dotnet $Cli status --index $expIndex) -join "`n"
if ($status -notmatch "modified      : False") { throw "restore must return to original`n$status" }

# Risk tiers on the CLI: the value option must parse, must validate, and --include-experimental
# must still reach the top tier — scripts written against the old boolean cannot silently narrow.
Write-Host "== CLI end-to-end: risk tiers =="
$tiers = Join-Path $Tmp "tiers"
$tiersBundles = Join-Path $tiers "Bundles2"
$tiersIndex = Join-Path $tiersBundles "_.index.bin"
$tiersPack = Join-Path $tiers "pack.json"
$tiersPet = "metadata/effects/spells/fire/spark.pet"

function Tier-Size {
    $read = (dotnet $Cli read-file --index $tiersIndex --path $tiersPet) -join "`n"
    if ($read -match "size\s*:\s*(\d+)") { return [int]$Matches[1] }
    throw "read-file did not report a size`n$read"
}

dotnet $Cli fixture --out $tiersBundles --path $tiersPet --content "PARTICLE-BYTES" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "tier fixture failed" }
dotnet $Cli build-rules --index $tiersIndex --out $tiersPack --name "Tier Test" --build synthetic | Out-Null
if ($LASTEXITCODE -ne 0) { throw "tier build-rules failed" }

dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental off --yes | Out-Null
if ((Tier-Size) -eq $petTemplate) { throw "--experimental off blanked $tiersPet" }

$apply = (dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --yes) -join "`n"
if ($apply -notmatch "files      : 1") { throw "--experimental fx should blank the particle rule`n$apply" }
if ((Tier-Size) -ne $petTemplate) { throw "--experimental fx did not write the template ($(Tier-Size) bytes)" }

dotnet $Cli plan --index $tiersIndex --pack $tiersPack --experimental everything | Out-Null
if ($LASTEXITCODE -eq 0) { throw "--experimental accepted a bogus tier" }

dotnet $Cli restore --index $tiersIndex --yes | Out-Null
$apply = (dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --include-experimental --yes) -join "`n"
if ($apply -notmatch "files      : 1") { throw "--include-experimental stopped meaning material`n$apply" }
if ((Tier-Size) -ne $petTemplate) { throw "--include-experimental did not write the template" }
dotnet $Cli restore --index $tiersIndex --yes | Out-Null

# Scoped restore: --undo-tiers material leaves the single fx entry suppressed (kept=1, still blank);
# --undo-tiers fx covers every entry and so takes the whole-index path. Assert outcomes.
dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --yes | Out-Null
$undo = (dotnet $Cli restore --index $tiersIndex --undo-tiers material --yes) -join "`n"
if ($undo -notmatch "kept=1") { throw "--undo-tiers material should keep the fx suppression`n$undo" }
if ((Tier-Size) -ne $petTemplate) { throw "--undo-tiers material undid the fx blanking" }

$undo = (dotnet $Cli restore --index $tiersIndex --undo-tiers fx --yes) -join "`n"
if ((Tier-Size) -eq $petTemplate) { throw "--undo-tiers fx did not give $tiersPet back`n$undo" }
if (Test-Path (Join-Path $env:POE2SMOOTHER_HOME "state.json")) { throw "a restore that undoes every entry must clear the state file" }

# The 0-byte fallback has to still do what it says, on the same fixture.
dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --blank-style empty --yes | Out-Null
if ((Tier-Size) -ne 0) { throw "--blank-style empty did not write a 0-byte file ($(Tier-Size) bytes)" }
# Assert the reason, not just the exit code: with no applied state everything below also fails.
$blankErr = (dotnet $Cli plan --index $tiersIndex --pack $tiersPack --experimental fx --blank-style sideways 2>&1) -join "`n"
if ($LASTEXITCODE -eq 0) { throw "--blank-style accepted a bogus value" }
if ($blankErr -notmatch "unknown --blank-style") { throw "--blank-style refused for the wrong reason`n$blankErr" }
dotnet $Cli restore --index $tiersIndex --yes | Out-Null

# Write strategies on the same fixture. The .pet is not a compression candidate, so it shows the
# blanking-off vs blanking-on split: compress-only never blanks it, compress-then-* still do.
$petOrig = Tier-Size
dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --blank-style compress-only --yes | Out-Null
if ((Tier-Size) -ne $petOrig) { throw "--blank-style compress-only blanked a non-candidate .pet ($(Tier-Size) bytes)" }
# No restore here: compress-only wrote 0 files, so there is no applied state to undo (restore
# would throw "No backup path is known", exactly like the --experimental off case above).

dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --blank-style compress-then-template --yes | Out-Null
if ((Tier-Size) -ne $petTemplate) { throw "--blank-style compress-then-template did not blank the .pet to a template" }
dotnet $Cli restore --index $tiersIndex --yes | Out-Null

dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --blank-style compress-then-empty --yes | Out-Null
if ((Tier-Size) -ne 0) { throw "--blank-style compress-then-empty did not write a 0-byte .pet" }
dotnet $Cli restore --index $tiersIndex --yes | Out-Null

# Class flags without an explicit strategy auto-promote to compress-then-template (old-script compat)
# and say so; the .pet still blanks because it is not a compression candidate.
$promo = (dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --compress-shader --yes 2>&1) -join "`n"
if ((Tier-Size) -ne $petTemplate) { throw "compression class flags without --blank-style should promote to compress-then-template and blank the .pet" }
if ($promo -notmatch "compress-then-template") { throw "promotion should note the chosen strategy`n$promo" }
dotnet $Cli restore --index $tiersIndex --yes | Out-Null

# health: the client log is the only party that knows whether a blanked asset actually loaded.
dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --yes | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $tiers "logs") | Out-Null
$log = @(
  '2026-01-01 00:00:00 000000 abcdef [WARN Client 1] Failed to create effect graph node "X" in graph "Metadata/Effects/Spells/Fire/Spark.PET"',
  '2026-01-01 00:00:01 000000 abcdef [WARN Client 1] Some unrelated warning about "Metadata/Art/Other/Untouched.mat"'
)
Set-Content -Path (Join-Path $tiers "logs/LatestClient.txt") -Value $log
$health = (dotnet $Cli health --index $tiersIndex) -join "`n"
$healthCode = $LASTEXITCODE
if ($healthCode -ne 7) { throw "health should flag the planted complaint (exit $healthCode)`n$health" }
if ($health -notmatch "suspect    : 1") { throw "health should find exactly one suspect`n$health" }
if ($health -notmatch "spark\.pet") { throw "health should name the blanked file`n$health" }
if ($health -match "Untouched\.mat") { throw "health must not blame files this tool never wrote`n$health" }
Remove-Item -Recurse -Force (Join-Path $tiers "logs")
dotnet $Cli restore --index $tiersIndex --yes | Out-Null

# Re-apply first: with no state the command would fail anyway and the test would pass for the
# wrong reason.
dotnet $Cli apply --index $tiersIndex --pack $tiersPack --preset safe --experimental fx --yes | Out-Null
dotnet $Cli restore --index $tiersIndex --undo-tiers fx --force --yes | Out-Null
if ($LASTEXITCODE -eq 0) { throw "--undo-tiers accepted --force" }
$forceErr = (dotnet $Cli restore --index $tiersIndex --undo-tiers fx --force --yes 2>&1) -join "`n"
if ($forceErr -notmatch "drop --force") { throw "--undo-tiers + --force refused for the wrong reason`n$forceErr" }
$tierErr = (dotnet $Cli restore --index $tiersIndex --undo-tiers everything --yes 2>&1) -join "`n"
if ($tierErr -notmatch "unknown --undo-tiers") { throw "--undo-tiers refused for the wrong reason`n$tierErr" }
dotnet $Cli restore --index $tiersIndex --yes | Out-Null

Write-Host ""
Write-Host "ALL TESTS PASSED"