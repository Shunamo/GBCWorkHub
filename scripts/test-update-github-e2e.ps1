param(
    [string]$MsBuild = "",
    [string]$WorkRoot = "artifacts\e2e-github-update",
    [int]$Port = 18765
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Find-MSBuild {
    param([string]$Hint)
    if ($Hint -and (Test-Path $Hint)) { return $Hint }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found) { return $found }
    }
    throw "MSBuild not found. Pass -MsBuild path."
}

function Get-FileVersion([string]$path) {
    return [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion
}

$msbuild = Find-MSBuild -Hint $MsBuild
$work = Join-Path $root $WorkRoot
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

Write-Host "== NuGet restore =="
$nuget = Join-Path $root "nuget.exe"
if (Test-Path $nuget) { & $nuget restore GBCWorkHub.sln } else { & $msbuild GBCWorkHub.sln /t:Restore /v:minimal }
if ($LASTEXITCODE -ne 0) { throw "nuget restore failed" }

function Build-Version([string]$ver, [string]$releaseBaseUrl) {
    & "$root\scripts\set-assembly-version.ps1" -Version $ver
    & $msbuild "src\GBCWorkHub.UI\GBCWorkHub.UI.csproj" /p:Configuration=Release /p:Platform=AnyCPU /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "UI build failed for $ver" }
    & $msbuild "tools\GBCWorkHub.Updater\GBCWorkHub.Updater.csproj" /p:Configuration=Release /p:Platform=AnyCPU /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Updater build failed for $ver" }

    $ui = "src\GBCWorkHub.UI\bin\Release\GBCWorkHub.UI.exe"
    $fv = Get-FileVersion $ui
    Write-Host "Built UI FileVersion=$fv"
    if ($fv -ne "$ver.0" -and $fv -ne $ver) { throw "FileVersion mismatch: $fv" }

    $packOut = Join-Path $work "pack-$ver"
    & "$root\scripts\pack-release.ps1" -Version $ver -Configuration Release -OutputDir $packOut -ReleaseBaseUrl $releaseBaseUrl
}

$owner = "e2e-owner"
$repo = "e2e-releases"
$releaseBase = "http://127.0.0.1:$Port/$owner/$repo"

Write-Host "== Build v1.0.1 (installed baseline) =="
Build-Version "1.0.1" $releaseBase
$install = Join-Path $work "install"
New-Item -ItemType Directory -Path $install | Out-Null
Copy-Item (Join-Path $work "pack-1.0.1\GBCWorkHub.UI.exe") $install
Copy-Item (Join-Path $work "pack-1.0.1\GBCWorkHubUpdater.exe") $install
Write-Host "Install FileVersion=$(Get-FileVersion (Join-Path $install 'GBCWorkHub.UI.exe'))"

Write-Host "== Build v1.0.2 (GitHub Release feed) =="
Build-Version "1.0.2" $releaseBase

$feedRoot = Join-Path $work "github-sim"
$latestDir = Join-Path $feedRoot "$owner\$repo\releases\latest\download"
$tagDir = Join-Path $feedRoot "$owner\$repo\releases\download\v1.0.2"
New-Item -ItemType Directory -Path $latestDir -Force | Out-Null
New-Item -ItemType Directory -Path $tagDir -Force | Out-Null
Copy-Item (Join-Path $work "pack-1.0.2\version.json") (Join-Path $latestDir "version.json")
Copy-Item (Join-Path $work "pack-1.0.2\GBCWorkHub-v1.0.2.zip") (Join-Path $tagDir "GBCWorkHub-v1.0.2.zip")

$manifestCheck = Get-Content (Join-Path $work "pack-1.0.2\version.json") -Raw | ConvertFrom-Json
$expectedPackageUrl = "$releaseBase/releases/download/v1.0.2/GBCWorkHub-v1.0.2.zip"
if ($manifestCheck.packageUrl -ne $expectedPackageUrl) {
    throw "packageUrl mismatch: $($manifestCheck.packageUrl)"
}
Write-Host "version.json packageUrl OK"
Write-Host "sha256: $($manifestCheck.sha256)"

$httpProc = Start-Process -FilePath "python" -ArgumentList @(
        "-m", "http.server", "$Port", "--bind", "127.0.0.1"
    ) -WorkingDirectory $feedRoot -PassThru -WindowStyle Hidden
Write-Host "Simulated GitHub Releases http://127.0.0.1:$Port/ (pid $($httpProc.Id) cwd=$feedRoot)"
Start-Sleep -Seconds 2
if ($httpProc.HasExited) { throw "http.server exited early code=$($httpProc.ExitCode)" }

try {
    $manifestUrl = "$releaseBase/releases/latest/download/version.json"
    Write-Host "== Manifest fetch =="
    Write-Host "GET $manifestUrl"
    $json = (Invoke-WebRequest -Uri $manifestUrl -UseBasicParsing -TimeoutSec 15).Content
    if ($json.Length -gt 0 -and [int][char]$json[0] -eq 0xFEFF) { $json = $json.Substring(1) }
    $remote = $json | ConvertFrom-Json
    if ($remote.version -ne "1.0.2") { throw "Remote version $($remote.version)" }
    Write-Host "Manifest OK version=$($remote.version)"

    Write-Host "== Package download + SHA256 =="
    $dl = Join-Path $work "downloaded-package.zip"
    Invoke-WebRequest -Uri $remote.packageUrl -OutFile $dl -UseBasicParsing -TimeoutSec 60
    $actualSha = (Get-FileHash -Path $dl -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha -ne $remote.sha256) { throw "SHA256 mismatch" }
    Write-Host "SHA256 OK"

    Write-Host "== TEMP Updater apply =="
    $session = [guid]::NewGuid().ToString("N")
    $tempUpdaterDir = Join-Path $env:TEMP "GBCWorkHubUpdater\$session"
    New-Item -ItemType Directory -Path $tempUpdaterDir -Force | Out-Null
    Copy-Item (Join-Path $install "GBCWorkHubUpdater.exe") (Join-Path $tempUpdaterDir "GBCWorkHubUpdater.exe")
    $workDir = Join-Path $env:TEMP "GBCWorkHubUpdater\$session-work"
    $argStr = "--package `"$dl`" --install-dir `"$install`" --sha256 $($remote.sha256) --work-dir `"$workDir`""
    $p = Start-Process -FilePath (Join-Path $tempUpdaterDir "GBCWorkHubUpdater.exe") -ArgumentList $argStr -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        $log = Join-Path $workDir "updater.log"
        if (Test-Path $log) { Get-Content $log }
        throw "Updater exit $($p.ExitCode)"
    }

    $after = Get-FileVersion (Join-Path $install "GBCWorkHub.UI.exe")
    Write-Host "After update FileVersion=$after"
    if ($after -ne "1.0.2.0" -and $after -ne "1.0.2") { throw "E2E failed FileVersion=$after" }

    Write-Host "E2E GITHUB-RELEASE UPDATE TEST PASSED (1.0.1 → 1.0.2)"
}
finally {
    if ($httpProc -and -not $httpProc.HasExited) {
        Stop-Process -Id $httpProc.Id -Force -ErrorAction SilentlyContinue
    }
    & "$root\scripts\set-assembly-version.ps1" -Version "1.0.0"
}
