# Disassemble IL of a .NET assembly using dnlib. Keep pure ASCII.
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$TypeFilter = "",
    [string]$MethodFilter = "",
    [switch]$Strings,
    [int]$Max = 400
)
$ErrorActionPreference = "Continue"
Add-Type -Path "C:\Users\Administrator\N.E.K.O\juno-bridge\tools\dl\dnlib.4.4.0\lib\net45\dnlib.dll"

$mod = [dnlib.DotNet.ModuleDefMD]::Load($Path)
Write-Host "### $Path"
$count = 0
foreach ($t in $mod.GetTypes()) {
    if ($TypeFilter -and ($t.FullName -notlike "*$TypeFilter*")) { continue }
    Write-Host "TYPE $($t.FullName)"
    if ($Strings) {
        foreach ($m in $t.Methods) {
            if (-not $m.HasBody) { continue }
            if ($MethodFilter -and ($m.Name -notlike "*$MethodFilter*")) { continue }
            foreach ($i in $m.Body.Instructions) {
                if ($i.OpCode.Name -eq "ldstr") { Write-Host ("    [str] {0}  ::  {1}" -f $m.Name, $i.Operand) }
            }
        }
        continue
    }
    foreach ($m in $t.Methods) {
        if ($MethodFilter -and ($m.Name -notlike "*$MethodFilter*")) { continue }
        if (-not $m.HasBody) { continue }
        $count++
        if ($count -gt $Max) { Write-Host "... (truncated)"; exit 0 }
        Write-Host ("  METHOD {0} {1}  ({2} instrs)" -f $m.ReturnType.TypeName, $m.Name, $m.Body.Instructions.Count)
        foreach ($i in $m.Body.Instructions) {
            Write-Host ("      {0,-12} {1}" -f $i.OpCode.Name, $i.Operand)
        }
    }
}
