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
if ((Exp-Size) -ne 0) { throw "--include-experimental did not blank $expPath" }

$restore = (dotnet $Cli restore --index $expIndex --yes) -join "`n"
if ($restore -notmatch "index restored") { throw "restore failed`n$restore" }
if ((Exp-Size) -ne $expected) { throw "restore did not give $expPath back" }
$status = (dotnet $Cli status --index $expIndex) -join "`n"
if ($status -notmatch "modified      : False") { throw "restore must return to original`n$status" }

Write-Host ""
Write-Host "ALL TESTS PASSED"