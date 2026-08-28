<#
  GBCWorkHub 스냅샷 백업 스크립트
  - 소스를 통째로 타임스탬프 zip으로 떠서 OneDrive 밖(로컬 디스크)에 저장한다.
  - bin/obj/packages/.vs 등 재생성 가능한 무거운 폴더는 제외한다.
  - 사용법: 이 스크립트를 PowerShell로 실행 (더블클릭 시 우클릭 > PowerShell로 실행)
    AI 에이전트에게 대규모 리팩터링을 시키기 "직전"에 한 번씩 실행하는 것을 권장.
#>

$ErrorActionPreference = 'Stop'

$projectRoot   = "C:\Users\ezcare\OneDrive - ezcaretech\바탕 화면\GBCWorkHub"
$backupRoot    = "C:\GBCWorkHub_Backups"   # OneDrive 동기화 범위 밖의 순수 로컬 경로
$retentionDays = 7                          # 이보다 오래된 백업 zip은 자동 삭제

if (-not (Test-Path $backupRoot)) {
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$zipPath   = Join-Path $backupRoot "GBCWorkHub_$timestamp.zip"
$stage     = Join-Path $env:TEMP "GBCWorkHub_backup_stage_$timestamp"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Write-Host "1/3 소스 복사 중 (bin/obj/packages/.vs 제외)..."
robocopy $projectRoot $stage /E /XD bin obj packages .vs /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    throw "robocopy 실패 (exit=$LASTEXITCODE)"
}

Write-Host "2/3 압축 중..."
Compress-Archive -Path "$stage\*" -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "3/3 임시 폴더 정리 중..."
Remove-Item $stage -Recurse -Force

$sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "완료: $zipPath ($sizeMb MB)"

Write-Host "4/4 오래된 백업(${retentionDays}일 초과) 정리 중..."
$cutoff = (Get-Date).AddDays(-$retentionDays)
$old = Get-ChildItem -Path $backupRoot -Filter "GBCWorkHub_*.zip" | Where-Object { $_.LastWriteTime -lt $cutoff }
foreach ($f in $old) {
    Remove-Item $f.FullName -Force
    Write-Host "  삭제됨: $($f.Name)"
}
if ($old.Count -eq 0) {
    Write-Host "  삭제 대상 없음"
}
