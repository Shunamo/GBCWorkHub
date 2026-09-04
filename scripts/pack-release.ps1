param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Configuration = "Release",

    [string]$OutputDir = "artifacts\release",

    # Public GitHub Releases base, e.g. https://github.com/Shunamo/GBCWorkHub-Releases
    [string]$ReleaseBaseUrl = "",

    [string]$ReleaseOwner = "",

    [string]$ReleaseRepo = ""
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

$zipPath = Join-Path $out $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath)

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
    version      = $v
    packageFile  = $zipName
    packageUrl   = $packageUrl
    sha256       = $sha
    releaseNotes = ""
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
