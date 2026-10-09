<#
.SYNOPSIS
    Kiểm tra toàn bộ schema nx_* trong solution so với spec ở docs/DATA-MODEL.md.

.DESCRIPTION
    Export solution rồi đọc customizations.xml, kiểm tra:
      - Đủ 5 bảng, đúng schema name
      - Date behavior của 7 cột ngày (1 = User Local, 3 = Date Only) — BẤT BIẾN
      - Lookup trùng lặp
    Chạy sau mỗi lần sửa schema trên portal.

.EXAMPLE
    .\scripts\verify-schema.ps1
#>
[CmdletBinding()]
param(
    [string]$Profile      = 'NexusDev',
    [string]$SolutionName = 'NexusLicensing'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$tmp      = Join-Path $repoRoot 'out\verify-schema'
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

pac auth select --name $Profile | Out-Null
$zip = Join-Path $tmp 'sol.zip'
pac solution export --name $SolutionName --path $zip --managed false --overwrite | Out-Null
Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp 'u') -Force

$xmlPath = Join-Path $tmp 'u\customizations.xml'
[xml]$cust = Get-Content $xmlPath
$fail = 0

Write-Host "--- BANG ---" -ForegroundColor Cyan
$wantTables = @('nx_LicensePlan','nx_Customer','nx_LicenseKey','nx_OrgRequest','nx_Activation')
$gotTables  = $cust.SelectNodes("//Entity/Name") | ForEach-Object { $_.'#text' }
foreach ($t in $wantTables) {
    if ($gotTables -contains $t) { Write-Host "OK       $t" -ForegroundColor Green }
    else { Write-Host "MISSING  $t" -ForegroundColor Red; $fail++ }
}

# Date behavior: 1 = User Local, 3 = Date Only. BAT BIEN - sai phai xoa cot tao lai.
Write-Host "`n--- DATE BEHAVIOR (bat bien) ---" -ForegroundColor Cyan
$wantDates = @{
    'nx_duedate'    = 3; 'nx_expiredate' = 3
    'nx_activatedon' = 1; 'nx_requestedon' = 1; 'nx_completedon' = 1; 'nx_actedon' = 1
}
foreach ($name in $wantDates.Keys | Sort-Object) {
    $node = $cust.SelectNodes("//attribute") | Where-Object { $_.LogicalName -eq $name } | Select-Object -First 1
    if (-not $node) { Write-Host "MISSING  $name" -ForegroundColor Red; $fail++; continue }
    $got  = "$($node.Behavior)"
    $want = "$($wantDates[$name])"
    $label = if ($want -eq '3') { 'Date Only' } else { 'User Local' }
    if ($got -eq $want) { Write-Host ("OK       {0,-16} Behavior={1} ({2})" -f $name,$got,$label) -ForegroundColor Green }
    else { Write-Host ("WRONG    {0,-16} Behavior={1}  can {2} ({3}) -- PHAI XOA COT TAO LAI" -f $name,$got,$want,$label) -ForegroundColor Red; $fail++ }
}

Write-Host "`n--- LOOKUP TRUNG (trong CUNG mot bang) ---" -ForegroundColor Cyan
# Cung ten lookup o HAI bang khac nhau la hop le (vd nx_licensekey o ca
# Activation lan OrgRequest). Chi bao loi khi trung trong cung mot bang.
$dupFound = $false
foreach ($ent in $cust.SelectNodes("//Entity")) {
    $entName = $ent.SelectSingleNode("Name").'#text'
    $lk = $ent.SelectNodes(".//attribute") | Where-Object { $_.Type -eq 'lookup' -and $_.LogicalName -like 'nx_*' }
    foreach ($g in ($lk | Group-Object { $_.LogicalName } | Where-Object { $_.Count -gt 1 })) {
        Write-Host ("DUP      {0} : {1} x{2}" -f $entName, $g.Name, $g.Count) -ForegroundColor Yellow
        $dupFound = $true
    }
}
if (-not $dupFound) { Write-Host "Khong co lookup trung trong cung bang." -ForegroundColor Green }


Write-Host "`n--- CASCADE DELETE (4 relationship tu tao) ---" -ForegroundColor Cyan
$wantRel = @{
    'nx_licensekey_Customer_nx_customer'       = 'Restrict'
    'nx_licensekey_Plan_nx_licenseplan'        = 'Restrict'
    'nx_orgrequest_LicenseKey_nx_licensekey'   = 'Restrict'
    'nx_activation_LicenseKey_nx_licensekey'   = 'Restrict'
}
foreach ($rel in $wantRel.Keys | Sort-Object) {
    $node = $cust.SelectNodes("//EntityRelationship") | Where-Object { $_.Name -eq $rel } | Select-Object -First 1
    if (-not $node) { Write-Host "MISSING  $rel" -ForegroundColor Red; $fail++; continue }
    $got = "$($node.EntityRelationshipType)"
    $del = $node.SelectSingleNode(".//CascadeDelete").'#text'
    if ($del -eq $wantRel[$rel]) { Write-Host ("OK       {0,-42} Delete={1}" -f $rel,$del) -ForegroundColor Green }
    else { Write-Host ("WRONG    {0,-42} Delete={1}  can {2}" -f $rel,$del,$wantRel[$rel]) -ForegroundColor Red; $fail++ }
}

Write-Host "`n--- ALTERNATE KEY ---" -ForegroundColor Cyan
$keys = $cust.SelectNodes("//EntityKey")
if ($keys.Count -eq 0) { Write-Host "MISSING  nx_uniquelicensekey (chua tao alternate key nao)" -ForegroundColor Red; $fail++ }
else { foreach ($k in $keys) { Write-Host "OK       $($k.Name)" -ForegroundColor Green } }

Write-Host ""
if ($fail -eq 0) { Write-Host "=== P1 HOAN TAT ===" -ForegroundColor Green }
else { Write-Host "Con $fail muc chua xong." -ForegroundColor Yellow }
