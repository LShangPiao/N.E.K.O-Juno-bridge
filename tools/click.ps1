# Send a mouse click (or key) to the Juno window. Keep pure ASCII.
param(
    [int]$X = -1,
    [int]$Y = -1,
    [string]$Key = "",
    [switch]$Activate
)
$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NekoInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint KEYUP = 0x0002;
}
"@

$proc = Get-Process -Name SimpleRockets2 -ErrorAction Stop | Select-Object -First 1
$h = $proc.MainWindowHandle

# WScript.Shell AppActivate is far more reliable than SetForegroundWindow
# from a background process.
try {
    $shell = New-Object -ComObject wscript.shell
    $ok = $shell.AppActivate($proc.Id)
    Write-Host "AppActivate: $ok"
} catch {
    Write-Host "AppActivate failed: $($_.Exception.Message)"
}
[NekoInput]::ShowWindow($h, 9) | Out-Null
[NekoInput]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 900

# Verify we really are foreground now
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NekoFg {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
}
"@
$fg = [NekoFg]::GetForegroundWindow()
$fgPid = 0
[NekoFg]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
Write-Host "foreground pid: $fgPid (game pid $($proc.Id))"


if ($Key -ne "") {
    $map = @{
        "ENTER" = 0x0D; "ESC" = 0x1B; "SPACE" = 0x20; "TAB" = 0x09;
        "UP" = 0x26; "DOWN" = 0x28; "LEFT" = 0x25; "RIGHT" = 0x27;
        "W" = 0x57; "A" = 0x41; "S" = 0x53; "D" = 0x44; "Z" = 0x5A; "X" = 0x58;
    }
    $vk = $map[$Key.ToUpper()]
    if (-not $vk) { throw "unknown key: $Key" }
    [NekoInput]::keybd_event([byte]$vk, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [NekoInput]::keybd_event([byte]$vk, 0, [NekoInput]::KEYUP, [IntPtr]::Zero)
    Write-Host "key sent: $Key"
}
elseif ($X -ge 0 -and $Y -ge 0) {
    [NekoInput]::SetCursorPos($X, $Y) | Out-Null
    Start-Sleep -Milliseconds 200
    [NekoInput]::mouse_event([NekoInput]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [NekoInput]::mouse_event([NekoInput]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Write-Host "clicked: $X,$Y"
}
else {
    throw "nothing to do"
}
Start-Sleep -Milliseconds 500
