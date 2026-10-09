# Reflection probe for Juno: New Origins ModApi assemblies.
# NOTE: keep this file pure ASCII - Windows PowerShell 5.1 reads BOM-less files as ANSI.
param(
    [string]$AsmName = "ModApi.Core.dll",
    [string]$Filter = "",
    [switch]$Members,
    [switch]$Constants,
    [switch]$Statics
)
$ErrorActionPreference = "Continue"
$refs = "C:\Users\Administrator\N.E.K.O\juno-bridge\refs"
$managed = "E:\Steam\steamapps\common\SimpleRockets2\SimpleRockets2_Data\Managed"

[System.AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({
    param($sender, $e)
    $simple = ($e.Name -split ',')[0]
    foreach ($root in @($refs, $managed)) {
        $p = Join-Path $root "$simple.dll"
        if (Test-Path $p) {
            try { return [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($p) } catch { }
        }
    }
    return $null
})

$path = Join-Path $refs $AsmName
if (-not (Test-Path $path)) { Write-Host "Assembly not found: $path"; exit 1 }
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($path)

$types = $null
try { $types = $asm.GetTypes() }
catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ } }

Write-Host "### Assembly: $($asm.GetName().Name)  Types: $($types.Count)"
if ($Filter) { $types = $types | Where-Object { $_.FullName -like "*$Filter*" } }

$flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::NonPublic
foreach ($t in ($types | Sort-Object FullName)) {
    if (-not $t) { continue }
    if ($Constants -or $Statics) {
        foreach ($f in $t.GetFields($flags)) {
            if ($Constants -and ($f.IsLiteral -or ($f.IsInitOnly -and $f.FieldType -eq [string]))) {
                $val = "<?>"
                try { $val = $f.GetRawConstantValue() } catch { try { $val = $f.GetValue($null) } catch { } }
                Write-Host ("CONST {0}.{1} = {2}" -f $t.FullName, $f.Name, $val)
            } elseif ($Statics -and $f.IsStatic) {
                Write-Host ("FIELD {0}.{1} : {2}" -f $t.FullName, $f.Name, $f.FieldType.FullName)
            }
        }
        continue
    }
    $kind = if ($t.IsInterface) { "interface" } elseif ($t.IsEnum) { "enum" } elseif ($t.IsValueType) { "struct" } else { "class" }
    $bt = if ($t.BaseType) { " : " + $t.BaseType.FullName } else { "" }
    Write-Host ("{0} {1}{2}" -f $kind, $t.FullName, $bt)
    if ($Members) {
        foreach ($m in $t.GetMembers([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly)) {
            Write-Host ("    {0}" -f $m)
        }
    }
}
