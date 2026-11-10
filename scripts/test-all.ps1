$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Tmp = Join-Path $Root ".test-all"
$Cli = Join-Path $Root "src/PsCli/bin/Release/net8.0/pscli.dll"
$Tests = Join-Path $Root "tests/PsTests/bin/Release/net8.0/pstests.dll"

Write-Host "== build =="
dotnet build (Join-Path $Root "PoE2Smoother.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }

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

Write-Host ""
Write-Host "ALL TESTS PASSED"