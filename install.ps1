# N.E.K.O-Juno-bridge installer
#
# Installs BepInEx (the mod loader) plus this plugin into Juno: New Origins.
#
# It does NOT touch any original game file: everything it adds lives in
#   <game>\winhttp.dll              (BepInEx loader proxy)
#   <game>\doorstop_config.ini
#   <game>\.doorstop_version
#   <game>\BepInEx\...              (loader + this plugin)
# To uninstall, delete exactly those. See README.md.
#
# NOTE: pure ASCII on purpose - Windows PowerShell 5.1 reads BOM-less files
# with the ANSI code page.

param(
    [string]$GameDir = "",
    [string]$BepInExZip = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

function Find-GameDir {
    param([string]$Explicit)
    if ($Explicit) {
        if (Test-Path (Join-Path $Explicit "SimpleRockets2.exe")) { return $Explicit }
        throw "GameDir does not contain SimpleRockets2.exe: $Explicit"
    }
    $candidates = @(
        "E:\Steam\steamapps\common\SimpleRockets2",
        "C:\Program Files (x86)\Steam\steamapps\common\SimpleRockets2",
        "D:\Steam\steamapps\common\SimpleRockets2",
        "D:\SteamLibrary\steamapps\common\SimpleRockets2",
        "E:\SteamLibrary\steamapps\common\SimpleRockets2",
        "F:\SteamLibrary\steamapps\common\SimpleRockets2"
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c "SimpleRockets2.exe")) { return $c }
    }
    throw "Could not locate Juno: New Origins. Pass -GameDir explicitly."
}

$Game = Find-GameDir -Explicit $GameDir
Write-Host "[install] game: $Game"

# --- 1. BepInEx ------------------------------------------------------------
$payload = Join-Path $Root "tools\bepinex-payload"
if (-not (Test-Path (Join-Path $payload "winhttp.dll"))) {
    if (-not $BepInExZip) {
        $BepInExZip = Join-Path $Root "tools\dl\BepInEx_win_x64_5.4.23.5.zip"
    }
    if (-not (Test-Path $BepInExZip)) {
        throw "BepInEx payload missing. Put BepInEx_win_x64_5.4.23.5.zip in tools\dl\ or pass -BepInExZip."
    }
    Write-Host "[install] expanding BepInEx from $BepInExZip"
    if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $payload | Out-Null
    Expand-Archive -Path $BepInExZip -DestinationPath $payload -Force
}

$running = Get-Process -Name "SimpleRockets2" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[install] WARNING: Juno: New Origins is running. Close it or the plugin will not load."
}

Write-Host "[install] copying BepInEx loader ..."
foreach ($item in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")) {
    $src = Join-Path $payload $item
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $Game $item) -Force
        Write-Host "          + $item"
    }
}

$srcCore = Join-Path $payload "BepInEx"
$dstCore = Join-Path $Game "BepInEx"
if (Test-Path $srcCore) {
    New-Item -ItemType Directory -Force -Path $dstCore | Out-Null
    Get-ChildItem $srcCore -Force | ForEach-Object {
        Copy-Item $_.FullName $dstCore -Recurse -Force
    }
    Write-Host "          + BepInEx\"
}

foreach ($sub in @("plugins", "config", "patchers", "monomod")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $dstCore $sub) | Out-Null
}

# --- 2. plugin -------------------------------------------------------------
Write-Host "[install] building plugin ..."
& (Join-Path $Root "build.ps1") -GameDir $Game -Install
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host ""
Write-Host "[install] done."
Write-Host ""
Write-Host "  Start the game and check the log:"
Write-Host "    $(Join-Path $Game 'BepInEx\LogOutput.log')"
Write-Host "  then open (while the game is running):"
Write-Host "    http://127.0.0.1:21577/ping"
Write-Host ""
Write-Host "  Uninstall: delete winhttp.dll, doorstop_config.ini, .doorstop_version and the BepInEx folder."
