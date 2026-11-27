# Build IndustryMaster.dll against a local Cities: Skylines II install.
#
# The game ships its own Mono profile (mscorlib.dll + netstandard.dll) in
# Cities2_Data/Managed, so the mod is compiled with the SDK's Roslyn csc against
# those assemblies instead of a .NET targeting pack.
#
# Usage: .\build\build.ps1 ["D:\steam\steamapps\common\Cities Skylines II"]
param([string]$GamePath = "D:\steam\steamapps\common\Cities Skylines II")
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$managed = Join-Path $GamePath "Cities2_Data\Managed"
if (-not (Test-Path $managed)) { throw "Managed folder not found: $managed" }

$sdkRoot = Join-Path (Split-Path (Get-Command dotnet).Source -Parent) "sdk"
$csc = Get-ChildItem (Join-Path $sdkRoot "*\Roslyn\bincore\csc.dll") | Sort-Object FullName | Select-Object -Last 1
if (-not $csc) { throw "csc.dll not found under $sdkRoot" }

$out = Join-Path $root "dist\IndustryMaster"
New-Item -ItemType Directory -Force -Path $out, (Join-Path $root "build\obj") | Out-Null
$rsp = Join-Path $root "build\obj\IndustryMaster.rsp"

$lines = @(
  "-nologo", "-nostdlib+", "-target:library", "-langversion:latest", "-unsafe+",
  "-optimize+", "-deterministic+", "-debug:portable",
  "-out:`"$(Join-Path $out 'IndustryMaster.dll')`""
)
$lines += Get-ChildItem $managed -Filter *.dll | ForEach-Object { "-r:`"$($_.FullName)`"" }
$lines += "-r:`"$(Join-Path $root 'lib\0Harmony.dll')`""
$lines += Get-ChildItem (Join-Path $root "source") -Filter *.cs | ForEach-Object { "`"$($_.FullName)`"" }

[System.IO.File]::WriteAllLines($rsp, $lines, (New-Object System.Text.UTF8Encoding($false)))
dotnet exec $csc.FullName "@$rsp"
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

Copy-Item (Join-Path $root "mod.json"), (Join-Path $root "IndustryMaster.mjs"), (Join-Path $root "lib\0Harmony.dll") $out -Force
Write-Host "Built: $out\IndustryMaster.dll"
