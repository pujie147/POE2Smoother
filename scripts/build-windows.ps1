#!/usr/bin/env pwsh
# Windows build: publish the GUI + CLI for win-x64 and pack portable zips.
# Runs on Windows (pwsh) and on the Linux CI runner (cross-publish), where the
# RID-driven vendor selection in Directory.Build.props picks the win-x64 natives.
param(
    [string]$Version = $env:PS_VERSION
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Rid = 'win-x64'
$StageRoot = Join-Path $Root 'build/windows'
$GuiDir = Join-Path $StageRoot 'PoE2Smoother'
$CliDir = Join-Path $StageRoot 'pscli'
$Dist = Join-Path $Root 'dist'
$GuiZip = Join-Path $Dist 'PoE2Smoother-win-x64.zip'
$CliZip = Join-Path $Dist 'pscli-win-x64.zip'

# PS_VERSION comes from CI (git tag with the leading "v" stripped). Unset = local build,
# which stays at the 0.0.0-dev fallback from Directory.Build.props.
$verArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $verArgs = @("-p:Version=$Version")
    Write-Host "== version: $Version =="
}

Remove-Item -Recurse -Force $StageRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $GuiDir -Force | Out-Null
New-Item -ItemType Directory -Path $CliDir -Force | Out-Null
New-Item -ItemType Directory -Path $Dist -Force | Out-Null

function Publish-App([string]$Project, [string]$OutDir) {
    dotnet publish $Project -c Release -r $Rid --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
        @verArgs -o $OutDir
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $Project" }
}

Write-Host '== publish GUI =='
Publish-App (Join-Path $Root 'src/PsApp/PsApp.csproj') $GuiDir

Write-Host '== publish CLI =='
Publish-App (Join-Path $Root 'src/PsCli/PsCli.csproj') $CliDir

Write-Host '== pack zips =='
Compress-Archive -Path $GuiDir -DestinationPath $GuiZip -Force
Compress-Archive -Path $CliDir -DestinationPath $CliZip -Force

Write-Host ''
Write-Host "GUI: $GuiZip"
Write-Host "CLI: $CliZip"