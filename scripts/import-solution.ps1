<#
.SYNOPSIS
    Pack solution từ source control và import vào một Dataverse environment.

.DESCRIPTION
    Dùng để đẩy solution lên environment khác (vd Nexus-Test).
    Quy tắc: Dev = unmanaged, Test/Prod = managed. Script mặc định pack MANAGED
    và sẽ cảnh báo nếu bạn cố import unmanaged vào profile không phải Dev.

.PARAMETER Profile
    Tên pac auth profile đích. Bắt buộc.

.PARAMETER SolutionName
    Unique name của solution. Mặc định NexusLicensing.

.PARAMETER Unmanaged
    Pack/import bản unmanaged thay vì managed. Chỉ dùng cho environment Dev.

.EXAMPLE
    .\scripts\import-solution.ps1 -Profile NexusTest
    .\scripts\import-solution.ps1 -Profile NexusDev -Unmanaged
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Profile,

    [string]$SolutionName = 'NexusLicensing',

    [switch]$Unmanaged
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$outDir    = Join-Path $repoRoot 'out'
$unpackDir = Join-Path $repoRoot "solution\$SolutionName"

if (-not (Test-Path $unpackDir)) {
    throw "Khong tim thay solution unpack tai '$unpackDir'. Chay export-solution.ps1 truoc."
}

$packageType = if ($Unmanaged) { 'Unmanaged' } else { 'Managed' }

if ($Unmanaged -and $Profile -notmatch 'Dev') {
    Write-Warning "Ban dang import UNMANAGED vao profile '$Profile'."
    Write-Warning "Quy tac ALM: chi Dev moi dung unmanaged. Test/Prod phai la managed."
    $answer = Read-Host "Van tiep tuc? (yes/no)"
    if ($answer -ne 'yes') { Write-Host "Da huy."; return }
}

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$suffix = if ($Unmanaged) { '' } else { '_managed' }
$zip    = Join-Path $outDir "${SolutionName}${suffix}_packed.zip"

Write-Host "==> Pack solution ($packageType)" -ForegroundColor Cyan
pac solution pack `
    --zipfile $zip `
    --folder  $unpackDir `
    --packagetype $packageType `
    --processCanvasApps
if (-not $?) { throw "Pack that bai." }

Write-Host "==> Chuyen sang auth profile '$Profile'" -ForegroundColor Cyan
pac auth select --name $Profile
if (-not $?) { throw "Khong chuyen duoc sang profile '$Profile'." }

Write-Host "==> Environment dich:" -ForegroundColor Cyan
pac org who

Write-Host "==> Import" -ForegroundColor Cyan
pac solution import `
    --path $zip `
    --publish-changes `
    --activate-plugins
if (-not $?) { throw "Import that bai. Xem solution import log tren Maker portal." }

Write-Host ""
Write-Host "HOAN TAT: da import $SolutionName ($packageType) vao '$Profile'." -ForegroundColor Green
