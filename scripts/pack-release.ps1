param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Configuration = "Release",

    [string]$OutputDir = "artifacts\release",

    # Public GitHub Releases base, e.g. https://github.com/Shunamo/GBCWorkHub-Releases
    [string]$ReleaseBaseUrl = "",

    [string]$ReleaseOwner = "",

    [string]$ReleaseRepo = "",

    # 이름이 GBCWorkHub.exe였던 예전 설치본의 자동 업데이트가 최소 한 번은
    # 안내를 보고 새 이름(GBCWorkHub.UI.exe)으로 넘어가도록 하는 호환용 안내 실행 파일.
    # 없으면(경로 미지정/파일 없음) 그냥 건너뛴다.
    [string]$LegacyStubExe = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$v = $Version.Trim()
if ($v.StartsWith("v") -or $v.StartsWith("V")) { $v = $v.Substring(1) }
$tag = "v$v"
$zipName = "GBCWorkHub-$tag.zip"

$uiExe = Join-Path $root "src\GBCWorkHub.UI\bin\$Configuration\GBCWorkHub.UI.exe"
$updaterExe = Join-Path $root "tools\GBCWorkHub.Updater\bin\$Configuration\GBCWorkHubUpdater.exe"

if (-not (Test-Path $uiExe)) { throw "Missing UI exe: $uiExe" }
if (-not (Test-Path $updaterExe)) { throw "Missing Updater exe: $updaterExe" }

if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $out = $OutputDir
} else {
    $out = Join-Path $root $OutputDir
}
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

$stage = Join-Path $out "stage"
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item $uiExe (Join-Path $stage "GBCWorkHub.UI.exe")
Copy-Item $updaterExe (Join-Path $stage "GBCWorkHubUpdater.exe")
if ($LegacyStubExe -and (Test-Path $LegacyStubExe)) {
    Copy-Item $LegacyStubExe (Join-Path $stage "GBCWorkHub.exe")
    Write-Host "Included legacy redirect stub as GBCWorkHub.exe"
}

$zipPath = Join-Path $out $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath)

# 사람이 직접 받아 새로 설치/재설치할 때 쓰는 순수 앱 zip — 자동 업데이트 패키지($zipName)와 달리
# 절대 legacy stub을 포함하지 않는다. stub이 없는 평소에는 위 zip과 내용이 같지만, 이번처럼
# 전환용 stub이 들어간 릴리즈에서도 신규 설치자가 헷갈리지 않도록 항상 별도로 만들어 둔다.
$setupZipName = "GBCWorkHub-Setup-$tag.zip"
$setupZipPath = Join-Path $out $setupZipName
if (Test-Path $setupZipPath) { Remove-Item $setupZipPath -Force }
if ($LegacyStubExe -and (Test-Path $LegacyStubExe)) {
    $cleanStage = Join-Path $out "stage-clean"
    New-Item -ItemType Directory -Path $cleanStage | Out-Null
    Copy-Item $uiExe (Join-Path $cleanStage "GBCWorkHub.UI.exe")
    Copy-Item $updaterExe (Join-Path $cleanStage "GBCWorkHubUpdater.exe")
    [System.IO.Compression.ZipFile]::CreateFromDirectory($cleanStage, $setupZipPath)
} else {
    Copy-Item $zipPath $setupZipPath
}
Write-Host "Packed $setupZipPath"

$sha = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

$base = $ReleaseBaseUrl.Trim().TrimEnd('/')
if (-not $base) {
    if ($ReleaseOwner -and $ReleaseRepo) {
        $base = "https://github.com/$ReleaseOwner/$ReleaseRepo"
    }
}
$packageUrl = ""
if ($base) {
    $packageUrl = "$base/releases/download/$tag/$zipName"
}

$manifest = [ordered]@{
    version         = $v
    packageFile     = $zipName
    packageUrl      = $packageUrl
    sha256          = $sha
    releaseNotes    = ""
    mainExeFileName = "GBCWorkHub.UI.exe"
}
$manifestPath = Join-Path $out "version.json"
$jsonText = ($manifest | ConvertTo-Json -Depth 5)
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifestPath, $jsonText, $utf8NoBom)

Write-Host "Packed $zipPath"
Write-Host "SHA256 $sha"
Write-Host "packageUrl $packageUrl"
Write-Host "Manifest $manifestPath"

Copy-Item $uiExe (Join-Path $out "GBCWorkHub.UI.exe")
Copy-Item $updaterExe (Join-Path $out "GBCWorkHubUpdater.exe")

# 사이트별 SessionAgent 설치 스크립트 — 앱 자동 업데이트 zip과는 별개로,
# 릴리즈에 부가 자산으로 첨부한다 (IT 담당자가 사이트 PC 설치 시 다운로드).
$siteSetupDir = Join-Path $root "tools\site-setup"
if (Test-Path $siteSetupDir) {
    $siteSetupZipName = "GBCWorkHub-SiteSetup-$tag.zip"
    $siteSetupZipPath = Join-Path $out $siteSetupZipName
    if (Test-Path $siteSetupZipPath) { Remove-Item $siteSetupZipPath -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($siteSetupDir, $siteSetupZipPath)
    Write-Host "Packed $siteSetupZipPath"
}
