# Extract printable strings (latin1 + utf16) from a binary. Keep pure ASCII.
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [int]$MinLen = 5,
    [string]$Pattern = ""
)
$bytes = [System.IO.File]::ReadAllBytes($Path)
$latin = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
$uni = [System.Text.Encoding]::Unicode.GetString($bytes)

$rx = "[\x20-\x7E]{" + $MinLen + ",}"
$all = New-Object System.Collections.Generic.List[string]
foreach ($m in [regex]::Matches($latin, $rx)) { $all.Add($m.Value) }
foreach ($m in [regex]::Matches($uni, $rx)) { $all.Add($m.Value) }
$all = $all | Sort-Object -Unique
if ($Pattern) { $all = $all | Where-Object { $_ -match $Pattern } }
$all | ForEach-Object { $_ }
Write-Host "### total: $($all.Count)"
