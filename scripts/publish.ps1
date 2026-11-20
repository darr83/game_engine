# Builds a ready-to-run copy of Game_Engine for Windows into dist\win-x64.
# Usage: .\scripts\publish.ps1            (or .\scripts\publish.ps1 -Rid win-arm64)
param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
$out = "dist\$Rid"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src/GameEngine.Editor -c Release -r $Rid --self-contained -o $out
dotnet publish src/GameEngine.Player -c Release -r $Rid --self-contained -o "$out\player"
Write-Host "Done: $out"
