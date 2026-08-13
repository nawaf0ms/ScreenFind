# Publishes ScreenFind as a single self-contained executable and packages it into a per-user MSI.
#
#   powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
#
# Requires the WiX CLI:  dotnet tool install --global wix
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> publishing ($Configuration)" -ForegroundColor Cyan
dotnet publish ScreenFind.App\ScreenFind.App.csproj -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$publishDir = Join-Path $root "ScreenFind.App\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64\publish"
$exe = Join-Path $publishDir "ScreenFind.exe"
if (-not (Test-Path $exe)) { throw "ScreenFind.exe not found in $publishDir" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "    ScreenFind.exe  $sizeMb MB"

$distDir = Join-Path $root "dist"
New-Item -ItemType Directory -Force $distDir | Out-Null
$msi = Join-Path $distDir "ScreenFind-$Version.msi"

Write-Host "==> building installer" -ForegroundColor Cyan
$wix = (Get-Command wix -ErrorAction SilentlyContinue)
if (-not $wix) {
    $candidate = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
    if (Test-Path $candidate) { $wix = $candidate } else { throw "wix CLI not found. Run: dotnet tool install --global wix" }
} else {
    $wix = $wix.Source
}

& $wix build (Join-Path $PSScriptRoot "ScreenFind.wxs") `
    -arch x64 `
    -ext WixToolset.Util.wixext `
    -ext WixToolset.UI.wixext `
    -d "PublishDir=$publishDir" `
    -d "IconFile=$(Join-Path $PSScriptRoot 'ScreenFind.ico')" `
    -d "Version=$Version" `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

Write-Host "==> done: $msi" -ForegroundColor Green
Write-Host "    install:   msiexec /i `"$msi`""
Write-Host "    uninstall: msiexec /x `"$msi`""
