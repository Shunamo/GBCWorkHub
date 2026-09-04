param(
    [string]$MsBuild = "",
    [string]$WorkRoot = "artifacts\e2e-update"
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
if (Test-Path $nuget) {
    & $nuget restore GBCWorkHub.sln
} else {
    & $msbuild GBCWorkHub.sln /t:Restore /v:minimal
}
if ($LASTEXITCODE -ne 0) { throw "nuget restore failed" }

function Build-Version([string]$ver) {
    & "$root\scripts\set-assembly-version.ps1" -Version $ver
    & $msbuild "src\GBCWorkHub.UI\GBCWorkHub.UI.csproj" /p:Configuration=Release /p:Platform=AnyCPU /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "UI build failed for $ver" }
    & $msbuild "tools\GBCWorkHub.Updater\GBCWorkHub.Updater.csproj" /p:Configuration=Release /p:Platform=AnyCPU /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Updater build failed for $ver" }

    $ui = "src\GBCWorkHub.UI\bin\Release\GBCWorkHub.UI.exe"
    $fv = Get-FileVersion $ui
    Write-Host "Built UI FileVersion=$fv (expected $ver.0 or $ver)"
    $normExpected = if ($ver -match '^\d+\.\d+\.\d+$') { "$ver.0" } else { $ver }
    if ($fv -ne $normExpected -and $fv -ne $ver) {
        throw "FileVersion mismatch: got $fv expected $normExpected"
    }

    $packOut = Join-Path $work "pack-$ver"
    & "$root\scripts\pack-release.ps1" -Version $ver -Configuration Release -OutputDir $packOut
}

Write-Host "== Build v1.0.0 (baseline install) =="
Build-Version "1.0.0"

$install = Join-Path $work "install"
New-Item -ItemType Directory -Path $install | Out-Null
Copy-Item (Join-Path $work "pack-1.0.0\GBCWorkHub.UI.exe") $install
Copy-Item (Join-Path $work "pack-1.0.0\GBCWorkHubUpdater.exe") $install
Write-Host "Install FileVersion=$(Get-FileVersion (Join-Path $install 'GBCWorkHub.UI.exe'))"

Write-Host "== Build v1.0.1 (update feed) =="
Build-Version "1.0.1"
$feed = Join-Path $work "feed"
New-Item -ItemType Directory -Path $feed | Out-Null
Copy-Item (Join-Path $work "pack-1.0.1\GBCWorkHub-v1.0.1.zip") $feed
Copy-Item (Join-Path $work "pack-1.0.1\version.json") $feed

# Simulate TEMP updater apply (self-update safe path)
$session = [guid]::NewGuid().ToString("N")
$tempUpdaterDir = Join-Path $env:TEMP "GBCWorkHubUpdater\$session"
New-Item -ItemType Directory -Path $tempUpdaterDir -Force | Out-Null
Copy-Item (Join-Path $install "GBCWorkHubUpdater.exe") (Join-Path $tempUpdaterDir "GBCWorkHubUpdater.exe")

$package = Join-Path $feed "GBCWorkHub-v1.0.1.zip"
$sha = (Get-Content (Join-Path $feed "version.json") -Raw | ConvertFrom-Json).sha256
$workDir = Join-Path $env:TEMP "GBCWorkHubUpdater\$session-work"
$launch = Join-Path $install "GBCWorkHub.UI.exe"

Write-Host "== Run TEMP updater =="
$argStr = "--package `"$package`" --install-dir `"$install`" --sha256 $sha --work-dir `"$workDir`""
$p = Start-Process -FilePath (Join-Path $tempUpdaterDir "GBCWorkHubUpdater.exe") -ArgumentList $argStr -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) {
    $log = Join-Path $workDir "updater.log"
    if (Test-Path $log) { Get-Content $log }
    throw "Updater exit code $($p.ExitCode)"
}

$after = Get-FileVersion $launch
Write-Host "After update FileVersion=$after"
if ($after -ne "1.0.1.0" -and $after -ne "1.0.1") {
    throw "E2E update failed: FileVersion=$after"
}

# Restore AssemblyInfo to 1.0.0.0 for local workspace hygiene
& "$root\scripts\set-assembly-version.ps1" -Version "1.0.0"

Write-Host "E2E UPDATE TEST PASSED"
