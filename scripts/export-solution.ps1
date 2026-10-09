<#
.SYNOPSIS
    Export solution NexusLicensing từ Dataverse và unpack vào source control.

.DESCRIPTION
    Chạy sau mỗi buổi làm việc trên cloud để đưa thay đổi về git.
    Export cả unmanaged (nguồn) và managed (để ALM biết cấu trúc managed).
    File .zip nằm trong out/ và đã được gitignore — chỉ bản unpack được commit.

.PARAMETER Profile
    Tên pac auth profile. Mặc định NexusDev.

.PARAMETER SolutionName
    Unique name của solution. Mặc định NexusLicensing.

.EXAMPLE
    .\scripts\export-solution.ps1
    .\scripts\export-solution.ps1 -Profile NexusDev -SolutionName NexusLicensing
#>
[CmdletBinding()]
param(
    [string]$Profile      = 'NexusDev',
    [string]$SolutionName = 'NexusLicensing'
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$outDir    = Join-Path $repoRoot 'out'
$unpackDir = Join-Path $repoRoot "solution\$SolutionName"

Write-Host "==> Chuyen sang auth profile '$Profile'" -ForegroundColor Cyan
pac auth select --name $Profile
if (-not $?) { throw "Khong chuyen duoc sang profile '$Profile'. Chay 'pac auth list' de kiem tra." }

Write-Host "==> Environment dang tro toi:" -ForegroundColor Cyan
pac org who

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$unmanagedZip = Join-Path $outDir "$SolutionName.zip"
$managedZip   = Join-Path $outDir "${SolutionName}_managed.zip"

Write-Host "==> Export unmanaged" -ForegroundColor Cyan
pac solution export --name $SolutionName --path $unmanagedZip --managed false --overwrite
if (-not $?) { throw "Export unmanaged that bai." }

Write-Host "==> Export managed" -ForegroundColor Cyan
pac solution export --name $SolutionName --path $managedZip --managed true --overwrite
if (-not $?) { throw "Export managed that bai." }

Write-Host "==> Unpack vao $unpackDir" -ForegroundColor Cyan
pac solution unpack `
    --zipfile $unmanagedZip `
    --folder  $unpackDir `
    --packagetype Both `
    --allowDelete `
    --processCanvasApps
if (-not $?) { throw "Unpack that bai." }

Write-Host ""
Write-Host "HOAN TAT. Kiem tra thay doi truoc khi commit:" -ForegroundColor Green
Write-Host "  git -C `"$repoRoot`" status" -ForegroundColor Gray
Write-Host "  git -C `"$repoRoot`" diff --stat" -ForegroundColor Gray
