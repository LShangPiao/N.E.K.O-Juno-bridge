# Package N.E.K.O-Juno-bridge into a drop-in zip.
# NOTE: keep this file pure ASCII (Windows PowerShell 5.1 reads BOM-less files as ANSI).
param(
    [string]$Version = "0.1.0",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Dist = Join-Path $Root "dist"
$Dll = Join-Path $Dist "JunoBridge.dll"
if (-not (Test-Path $Dll)) { throw "Build first: .\build.ps1" }

$Payload = Join-Path $Root "tools\bepinex-payload"
if (-not (Test-Path (Join-Path $Payload "winhttp.dll"))) {
    throw "BepInEx payload missing at $Payload. Run install.ps1 once, or see tools\README.md."
}

if (-not $OutDir) { $OutDir = $Dist }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$stage = Join-Path $env:TEMP "neko-juno-bridge-stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# 1) BepInEx loader at the game root
Write-Host "[package] staging BepInEx loader ..."
foreach ($f in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")) {
    Copy-Item (Join-Path $Payload $f) (Join-Path $stage $f) -Force
}
Copy-Item (Join-Path $Payload "BepInEx") (Join-Path $stage "BepInEx") -Recurse -Force

# 2) our plugin
$pluginDir = Join-Path $stage "BepInEx\plugins\N.E.K.O-Juno-bridge"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item $Dll (Join-Path $pluginDir "JunoBridge.dll") -Force

# 3) instructions
$readme = @"
N.E.K.O-Juno-bridge v$Version  (Neko Uplink)
Read-only flight telemetry for Juno: New Origins, over a local HTTP API.
https://github.com/LShangPiao/N.E.K.O-Juno-bridge

HOW TO INSTALL
--------------
Copy everything in this zip into the game folder, next to SimpleRockets2.exe.
Typical Steam path:
  E:\Steam\steamapps\common\SimpleRockets2

After copying the folder should contain:
  SimpleRockets2.exe
  winhttp.dll               <- added
  doorstop_config.ini       <- added
  .doorstop_version         <- added
  BepInEx\                  <- added
  SimpleRockets2_Data\

Then start the game and open:
  http://127.0.0.1:21577/ping

The plugin logs to:
  BepInEx\plugins\N.E.K.O-Juno-bridge\juno-bridge.log
  BepInEx\LogOutput.log

CONFIG
------
BepInEx\plugins\N.E.K.O-Juno-bridge\neko-juno-bridge.ini
  port=21577              HTTP port (localhost only)
  poll_interval_ms=200    telemetry sampling interval, 50-60000
  log_to_file=true

UNINSTALL
---------
Delete winhttp.dll, doorstop_config.ini, .doorstop_version and the BepInEx
folder. No original game file is ever modified.

READ-ONLY
---------
This mod only serves GET requests and never changes game state.
"@
[System.IO.File]::WriteAllText((Join-Path $stage "README.txt"), $readme, (New-Object System.Text.UTF8Encoding($false)))

# 4) zip
$zip = Join-Path $OutDir "N.E.K.O-Juno-Bridge-v$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Write-Host "[package] wrote $zip ($((Get-Item $zip).Length) bytes)"

# checksum - same step as the sibling SP2 bridge package
$sha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$sums = Join-Path $OutDir "SHA256SUMS.txt"
Set-Content -LiteralPath $sums -Value ("$sha  $(Split-Path $zip -Leaf)") -Encoding ASCII
Write-Host "[package] SHA256: $sha"

Write-Host "[package] contents:"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($e in $archive.Entries) { Write-Host ("   {0}  ({1})" -f $e.FullName, $e.Length) }
} finally { $archive.Dispose() }

Remove-Item $stage -Recurse -Force
Write-Host "[package] done."
