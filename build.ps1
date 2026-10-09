# N.E.K.O-Juno-bridge build script
#
# Compiles the BepInEx plugin against the game's own assemblies and the
# Roslyn compiler downloaded into tools/dl/roslyn.
#
# NOTE: This file is intentionally pure ASCII. Windows PowerShell 5.1 reads
# BOM-less files using the ANSI code page, so non-ASCII text here would break
# parsing on some machines (a lesson learned the hard way on the KSP project).
#
# Usage:
#   .\build.ps1                       # compile only -> dist\JunoBridge.dll
#   .\build.ps1 -Install              # compile and copy into the game
#   .\build.ps1 -GameDir "D:\Games\SimpleRockets2"

param(
    [string]$GameDir = "",
    [switch]$Install,
    [switch]$Clean,
    [switch]$Debug
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "src"
$Dist = Join-Path $Root "dist"
$Csc = Join-Path $Root "tools\dl\roslyn\tasks\net472\csc.exe"
$PluginName = "N.E.K.O-Juno-bridge"
$DllName = "JunoBridge.dll"

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

if (-not (Test-Path $Csc)) {
    throw "Roslyn compiler not found at $Csc. See tools\README.md for how to fetch it."
}

$Game = Find-GameDir -Explicit $GameDir
$Managed = Join-Path $Game "SimpleRockets2_Data\Managed"
Write-Host "[juno-bridge] game      : $Game"
Write-Host "[juno-bridge] managed   : $Managed"

if ($Clean) {
    foreach ($f in @($Dist, (Join-Path $Src "obj"))) {
        if (Test-Path $f) { Remove-Item $f -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
New-Item -ItemType Directory -Force -Path $Dist | Out-Null

# --- references ------------------------------------------------------------
$refs = @(
    "mscorlib.dll",
    "System.dll",
    "System.Core.dll",
    "netstandard.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.PhysicsModule.dll",
    "UnityEngine.JSONSerializeModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "UnityEngine.UI.dll",
    "UnityEngine.UIModule.dll",
    "SimpleRockets2.dll",
    "ModApi.dll",
    "ModApi.Core.dll",
    "Jundroo.ModTools.dll",
    "Jundroo.ModTools.Core.dll",
    "ThirdParty.dll"
)

$refArgs = @()
foreach ($r in $refs) {
    $p = Join-Path $Managed $r
    if (Test-Path $p) { $refArgs += "-r:$p" }
    else { Write-Host "[juno-bridge] warn: missing reference $r" }
}

# BepInEx provides BaseUnityPlugin / BepInPlugin
$BepInExDll = Join-Path $Root "tools\dl\bepinex\BepInEx\core\BepInEx.dll"
if (-not (Test-Path $BepInExDll)) {
    throw "BepInEx.dll not found at $BepInExDll. See tools\README.md."
}
$refArgs += "-r:$BepInExDll"
Write-Host "[juno-bridge] bepinex   : $BepInExDll"

# --- compile ---------------------------------------------------------------
$sources = Get-ChildItem -Path $Src -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "No sources found in $Src" }

$outDll = Join-Path $Dist $DllName
$cscArgs = @(
    "-nologo",
    "-target:library",
    "-langversion:7.3",
    "-optimize+",
    "-debug-",
    "-nostdlib+",
    "-deterministic+",
    "-out:$outDll"
) + $refArgs + $sources

if ($Debug) {
    $cscArgs += "-define:NEKO_DEBUG"
    Write-Host "[juno-bridge] DEBUG build (debug probe included)"
}

Write-Host "[juno-bridge] compiling $($sources.Count) source files ..."
& $Csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

$info = Get-Item $outDll
Write-Host "[juno-bridge] built      : $($info.FullName) ($($info.Length) bytes)"

# --- install ---------------------------------------------------------------
$pluginDir = Join-Path $Game "BepInEx\plugins\$PluginName"
if ($Install) {
    Write-Host "[juno-bridge] installing ..."
    $drop = Join-Path $Game "BepInEx"
    if (-not (Test-Path $drop)) {
        throw "BepInEx is not installed in $Game. Copy the BepInEx folder there first (see install.ps1)."
    }
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    Copy-Item $outDll (Join-Path $pluginDir $DllName) -Force
    Write-Host "[juno-bridge] installed  : $pluginDir\$DllName"
}
else {
    Write-Host "[juno-bridge] plugin dir would be: $pluginDir"
}

Write-Host "[juno-bridge] done."
