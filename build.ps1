#!/usr/bin/env pwsh
# Build the mod (Release) and package a distributable zip into dist/.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "src/PlayerModelSwap/TimeStranger.PlayerModelSwap.csproj"
$out  = Join-Path $root "dist/timestranger.noah.playermodelswap"
Remove-Item -Recurse -Force (Join-Path $root "dist") -ErrorAction SilentlyContinue
dotnet build $proj -c Release -o $out
$zip = Join-Path $root "dist/timestranger.noah.playermodelswap.zip"
Compress-Archive -Path $out -DestinationPath $zip -Force
Write-Host "Packaged: $zip"
