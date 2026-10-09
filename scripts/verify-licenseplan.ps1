<#
.SYNOPSIS
    Kiểm tra Min/Max/MaxLength của các cột nx_licenseplan sau khi sửa tay trên portal.

.DESCRIPTION
    Export solution rồi đọc customizations.xml, so sánh với spec ở docs/DATA-MODEL.md §1.
    Dùng sau khi chỉnh cột trong solution editor để xác nhận giá trị đã vào Dataverse.

.EXAMPLE
    .\scripts\verify-licenseplan.ps1
#>
[CmdletBinding()]
param(
    [string]$Profile      = 'NexusDev',
    [string]$SolutionName = 'NexusLicensing'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$tmp      = Join-Path $repoRoot 'out\verify'
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

pac auth select --name $Profile | Out-Null
$zip = Join-Path $tmp 'sol.zip'
pac solution export --name $SolutionName --path $zip --managed false --overwrite | Out-Null
Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp 'u') -Force

[xml]$cust = Get-Content (Join-Path $tmp 'u\customizations.xml')

# spec: column -> @{ MaxLength / MinValue / MaxValue }
$spec = @{
    'nx_planname'       = @{ MaxLength = 100 }
    'nx_plancode'       = @{ MaxLength = 20 }
    'nx_repocapacity'   = @{ MinValue = 1; MaxValue = 1000 }
    'nx_durationmonths' = @{ MinValue = 1; MaxValue = 120 }
    'nx_gracedays'      = @{ MinValue = 0; MaxValue = 90 }
}

$attrs = $cust.SelectNodes("//attribute")
$fail  = 0

foreach ($name in $spec.Keys | Sort-Object) {
    $node = $attrs | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if (-not $node) { Write-Host "MISSING  $name" -ForegroundColor Red; $fail++; continue }

    foreach ($prop in $spec[$name].Keys) {
        $want = $spec[$name][$prop]
        $got  = $node.$prop
        if ("$got" -eq "$want") {
            Write-Host ("OK       {0,-20} {1,-10} = {2}" -f $name, $prop, $got) -ForegroundColor Green
        } else {
            Write-Host ("WRONG    {0,-20} {1,-10} = {2}  (can: {3})" -f $name, $prop, $got, $want) -ForegroundColor Red
            $fail++
        }
    }
}

Write-Host ""
if ($fail -eq 0) { Write-Host "TAT CA DUNG SPEC." -ForegroundColor Green }
else { Write-Host "Con $fail muc chua dung." -ForegroundColor Yellow }
