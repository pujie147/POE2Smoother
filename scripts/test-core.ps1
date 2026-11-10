$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $Root "PoE2Smoother.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }
dotnet (Join-Path $Root "tests/PsTests/bin/Release/net8.0/pstests.dll")
if ($LASTEXITCODE -ne 0) { throw "core tests failed" }
Write-Host "CORE TESTS PASSED"
