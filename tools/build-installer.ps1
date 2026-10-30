# Builds the OptimAdhan MSI: runs the tests, publishes the app, then packages it with WiX v5.
# The MSI needs no prerequisites on the user's PC. Build prerequisites: .NET 10 SDK; WiX v5 tool (dotnet tool install -g wix --version 5.0.2) with
# the Util extension (wix extension add -g WixToolset.Util.wixext/5.0.2).
# Usage (from the workspace root):  powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
param([switch] $SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $root 'src\OptimAdhan.App\OptimAdhan.App.csproj'
$publishDir = Join-Path $root 'artifacts\publish'
$outDir = Join-Path $root 'artifacts\installer'

[xml] $proj = Get-Content $appProject
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in the app project.' }

if (-not $SkipTests) {
    Write-Host '== Running tests' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'tests\OptimAdhan.Core.Tests') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; installer not built.' }
}

Write-Host "== Publishing OptimAdhan $version" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# Self-contained: the .NET 10 runtime (with WPF and WinForms) ships inside the app folder, so nothing
# needs to be installed first. Not trimmed (WPF does not support trimming) and not single-file
# (loose files start faster and compress well in the MSI cabinet).
dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:DebugType=none -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Write-Host '== Building MSI' -ForegroundColor Cyan
New-Item -ItemType Directory -Force $outDir | Out-Null
$msi = Join-Path $outDir "OptimAdhan-$version-x64.msi"
wix build (Join-Path $root 'installer\Package.wxs') -arch x64 -ext WixToolset.Util.wixext `
    -d "Version=$version" -d "PublishDir=$publishDir" `
    -d "IconFile=$(Join-Path $root 'src\OptimAdhan.App\Assets\Brand\optimadhan.ico')" -o $msi
if ($LASTEXITCODE -ne 0) { throw 'WiX build failed.' }

Write-Host '== Validating MSI (ICE)' -ForegroundColor Cyan
# ICE38/64/91 concern per-user profile folders and do not apply to a per-user-only package.
wix msi validate $msi -sice ICE38 -sice ICE64 -sice ICE91
if ($LASTEXITCODE -ne 0) { throw 'MSI validation failed.' }

$size = (Get-Item $msi).Length / 1MB
Write-Host ("== Done: {0} ({1:N1} MB)" -f $msi, $size) -ForegroundColor Green
