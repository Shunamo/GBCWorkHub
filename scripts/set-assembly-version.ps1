param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string[]]$AssemblyInfoPaths = @(
        "src\GBCWorkHub.UI\Properties\AssemblyInfo.cs",
        "tools\GBCWorkHub.Updater\Properties\AssemblyInfo.cs"
    )
)

$ErrorActionPreference = "Stop"

$v = $Version.Trim()
if ($v.StartsWith("v") -or $v.StartsWith("V")) {
    $v = $v.Substring(1)
}
if ($v -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Invalid version: $Version (expected like 1.2.0 or v1.2.0)"
}
$parts = $v.Split('.')
if ($parts.Length -eq 3) {
    $v = "$v.0"
}

$root = Split-Path -Parent $PSScriptRoot
foreach ($rel in $AssemblyInfoPaths) {
    $path = Join-Path $root $rel
    if (-not (Test-Path $path)) {
        throw "AssemblyInfo not found: $path"
    }
    $content = Get-Content -Path $path -Raw -Encoding UTF8
    $content = [regex]::Replace($content, 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$v`")")
    $content = [regex]::Replace($content, 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$v`")")
    Set-Content -Path $path -Value $content -Encoding UTF8 -NoNewline
    Write-Host "Injected $v into $rel"
}

Write-Host "Assembly version set to $v"
