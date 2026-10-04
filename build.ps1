# Builds Slayer Tools and copies the DLL into BepInEx\plugins.
#   .\build.ps1
#   .\build.ps1 -NoInstall
#   .\build.ps1 -BepInExDir "D:\SteamLibrary\steamapps\common\Idle Slayer\BepInEx"
param(
    [string]$BepInExDir = "C:\Program Files (x86)\Steam\steamapps\common\Idle Slayer\BepInEx",
    [switch]$NoInstall
)
$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host ".NET SDK not found. Install it with: winget install Microsoft.DotNet.SDK.8" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path (Join-Path $BepInExDir "interop\Assembly-CSharp.dll"))) {
    Write-Host "BepInEx\interop not found in $BepInExDir" -ForegroundColor Red
    Write-Host "Start the game once with BepInEx installed to generate it, or pass -BepInExDir." -ForegroundColor Yellow
    exit 1
}
if (-not $NoInstall -and (Get-Process "Idle Slayer" -ErrorAction SilentlyContinue)) {
    Write-Host "Close Idle Slayer first (the DLL is locked while the game is running)." -ForegroundColor Yellow
    exit 1
}

$install = if ($NoInstall) { "false" } else { "true" }
dotnet build "$PSScriptRoot\src\SlayerTools.csproj" -c Release "-p:BepInExDir=$BepInExDir" "-p:Install=$install"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Done!" -ForegroundColor Green
