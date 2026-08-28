#requires -Version 5.0
<#
.SYNOPSIS
  Aurora single SessionAgent (same role as RC/CMC exe).
  Replaces Collect-RdpStatus / Collect-TfsRecent3 / Handle-RdpConnect /
  Send-PendingTfsOnReconnect / Collect-RdpDisconnect.ps1

.DESCRIPTION
  Trigger (disconnect not needed; fetch = reconnect connect):
    powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File C:\GBCWorkHub\Aurora_SessionAgent.ps1 connect

  TFS has no REST _apis here -> Team Explorer OM (GAC) QueryHistory.
  Keep this file ASCII-only (no Korean) so copy/encoding cannot break parsing.
#>
param(
    [Parameter(Position = 0)]
    [string]$Action = 'connect'
)

# No StrictMode: missing JSON props under scheduler often caused 0x1
$ErrorActionPreference = 'Stop'
$Action = ([string]$Action).Trim().ToLowerInvariant()
if ($Action -notin @('connect', 'disconnect', 'test')) {
    $Action = 'connect'
}

# Earliest failure breadcrumb (even before functions)
try {
    $bootLogDir = 'C:\GBCWorkHub\Logs'
    if (-not (Test-Path -LiteralPath $bootLogDir)) {
        New-Item -ItemType Directory -Path $bootLogDir -Force | Out-Null
    }
    Add-Content -LiteralPath (Join-Path $bootLogDir 'SessionAgent.log') -Encoding UTF8 `
        -Value ('{0} | BOOT | action={1} file={2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Action, $PSCommandPath)
}
catch { }

trap {
    try {
        $msg = $_.Exception.Message
        $line = $_.InvocationInfo.ScriptLineNumber
        Add-Content -LiteralPath 'C:\GBCWorkHub\Logs\SessionAgent.log' -Encoding UTF8 `
            -Value ('{0} | TRAP | line={1} | {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $line, $msg)
        $_ | Out-String | Set-Content -LiteralPath 'C:\GBCWorkHub\SessionAgentError.log' -Encoding UTF8
    }
    catch { }
    break
}

# ============================================================
# Configuration (edit per site)
# ============================================================
$BaseDirectory = 'C:\GBCWorkHub'
$CollectionUrl = 'http://172.20.0.90:8080/tfs/bestcare2.0b_v1.0'
$ServerPath = '$/HISSolutions'
$TfsMaxChangesets = 100
$TfsFallbackTodayMax = 50

$TfsClientDllDefault = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.TeamFoundation.Client\v4.0_12.0.0.0__b03f5f7f11d50a3a\Microsoft.TeamFoundation.Client.dll'
$TfsVcDllDefault = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.TeamFoundation.VersionControl.Client\v4.0_12.0.0.0__b03f5f7f11d50a3a\Microsoft.TeamFoundation.VersionControl.Client.dll'

$RdpPrefix = 'GBCWORKHUB::'
$TfsPrefix = 'GBCWORKHUB_TFS::'
$SyncRequestPrefix = 'GBCWORKHUB_TFS_SYNC_REQUEST::'
$AckPrefix = 'GBCWORKHUB_ACK::'
$PendingFileName = 'PendingTfsRecent.json'
$SessionStartedFileName = 'SessionStartedUtc.txt'
$LogFileName = 'SessionAgent.log'

# ============================================================
# Paths / init
# ============================================================
$LogDirectory = Join-Path $BaseDirectory 'Logs'
$PendingPath = Join-Path $BaseDirectory $PendingFileName
$SessionStartedPath = Join-Path $BaseDirectory $SessionStartedFileName
$LastStatusPath = Join-Path $BaseDirectory 'LastStatus.json'
$LogPath = Join-Path $LogDirectory $LogFileName
$ErrorLogPath = Join-Path $BaseDirectory 'SessionAgentError.log'

New-Item -ItemType Directory -Path $BaseDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null

$script:UserClipboardBackup = $null

# ============================================================
# Helpers
# ============================================================
function Write-AgentLog {
    param([string]$Message)
    try {
        $line = '{0} | {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
        Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    }
    catch { }
}

function Write-AgentError {
    param($ErrorRecord)
    try {
        $text = @(
            (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
            ($ErrorRecord | Out-String)
            '--------------------------------'
        ) -join [Environment]::NewLine
        Add-Content -LiteralPath $ErrorLogPath -Value $text -Encoding UTF8
    }
    catch { }
}

function Write-JsonFile {
    param([object]$Value, [string]$Path, [int]$Depth = 40)
    $tmp = "$Path.tmp"
    ($Value | ConvertTo-Json -Depth $Depth -Compress) |
        Set-Content -LiteralPath $tmp -Encoding UTF8
    Move-Item -LiteralPath $tmp -Destination $Path -Force
}

function Test-ProtocolText {
    param([string]$Text)
    return (-not [string]::IsNullOrEmpty($Text)) -and $Text.StartsWith('GBCWORKHUB', [StringComparison]::Ordinal)
}

function Get-ClipboardTextSafe {
    try {
        return [string](Get-Clipboard -Raw -ErrorAction Stop)
    }
    catch {
        return $null
    }
}

function Set-ClipboardTextSafe {
    param([string]$Text)
    for ($i = 0; $i -lt 5; $i++) {
        try {
            Set-Clipboard -Value $Text -ErrorAction Stop
            return $true
        }
        catch {
            Start-Sleep -Milliseconds 200
        }
    }
    return $false
}

function Clear-ProtocolClipboard {
    $current = Get-ClipboardTextSafe
    if ([string]::IsNullOrEmpty($current) -or -not (Test-ProtocolText $current)) {
        return
    }
    for ($i = 0; $i -lt 5; $i++) {
        try {
            Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
            [System.Windows.Forms.Clipboard]::Clear()
            return
        }
        catch {
            try {
                cmd.exe /c 'echo.| clip' | Out-Null
                return
            }
            catch { }
            Start-Sleep -Milliseconds 200
        }
    }
}

function Restore-UserClipboard {
    $backup = $script:UserClipboardBackup
    # No backup: leave GBCWORKHUB* stuck and paste shows protocol only
    if ([string]::IsNullOrEmpty($backup)) {
        Clear-ProtocolClipboard
        return
    }

    $current = Get-ClipboardTextSafe
    if (-not [string]::IsNullOrEmpty($current) -and
        -not (Test-ProtocolText $current) -and
        $current -cne $backup) {
        return
    }

    if (-not (Set-ClipboardTextSafe $backup)) {
        Clear-ProtocolClipboard
    }
}

function Send-ClipboardPayload {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [switch]$IsTfs
    )

    $cur = Get-ClipboardTextSafe
    if (-not [string]::IsNullOrEmpty($cur) -and -not (Test-ProtocolText $cur)) {
        $script:UserClipboardBackup = $cur
    }

    if (-not (Set-ClipboardTextSafe $Text)) {
        return $false
    }

    if ($IsTfs) {
        # Wait for ACK / user copy / timeout, then restore (keep short so scheduler ends)
        $minHoldMs = 1500
        $maxWaitMs = 6000
        $stepMs = 250
        Start-Sleep -Milliseconds $minHoldMs
        $waited = $minHoldMs
        while ($waited -lt $maxWaitMs) {
            $now = Get-ClipboardTextSafe
            if (-not [string]::IsNullOrEmpty($now) -and -not (Test-ProtocolText $now)) {
                return $true
            }
            if (-not [string]::IsNullOrEmpty($now) -and $now.StartsWith($AckPrefix, [StringComparison]::Ordinal)) {
                break
            }
            Start-Sleep -Milliseconds $stepMs
            $waited += $stepMs
        }
        Restore-UserClipboard
    }
    else {
        Start-Sleep -Milliseconds 800
        Restore-UserClipboard
    }
    return $true
}

function ConvertTo-CompactJson {
    param([object]$Value, [int]$Depth = 40)
    return ($Value | ConvertTo-Json -Depth $Depth -Compress)
}

function Try-ParseDateTimeUtc {
    param([string]$Value, [ref]$Result)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    $dto = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Value, [ref]$dto)) { return $false }
    $Result.Value = $dto.UtcDateTime
    return $true
}

function Resolve-TfsDllPath {
    param([string]$AssemblyName, [string]$Preferred)
    if (Test-Path -LiteralPath $Preferred) { return $Preferred }
    $root = Join-Path 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL' $AssemblyName
    if (-not (Test-Path -LiteralPath $root)) { return $Preferred }
    $dirs = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending)
    foreach ($d in $dirs) {
        $dll = Join-Path $d.FullName ($AssemblyName + '.dll')
        if (Test-Path -LiteralPath $dll) { return $dll }
    }
    return $Preferred
}

function Get-FirstProp {
    param($Object, [string[]]$Names)
    if ($null -eq $Object) { return $null }
    foreach ($n in $Names) {
        $p = $Object.PSObject.Properties[$n]
        if ($null -eq $p -or $null -eq $p.Value) { continue }
        $s = [string]$p.Value
        if (-not [string]::IsNullOrWhiteSpace($s)) { return $p.Value }
    }
    return $null
}

function Normalize-ChangeType {
    param([string]$ChangeType)
    if ([string]::IsNullOrWhiteSpace($ChangeType)) { return 'edit' }
    $c = $ChangeType.Trim().ToLowerInvariant()
    $flags = 0
    if ([int]::TryParse($c, [ref]$flags)) {
        if (($flags -band 16) -ne 0) { return 'delete' }
        if (($flags -band 8) -ne 0) { return 'rename' }
        if (($flags -band 1) -ne 0) { return 'add' }
        return 'edit'
    }
    if ($c -like '*add*' -or $c -like '*branch*') { return 'add' }
    if ($c -like '*delete*') { return 'delete' }
    if ($c -like '*rename*') { return 'rename' }
    return 'edit'
}

# ============================================================
# RDP status / SYNC request
# ============================================================
function New-RdpStatusObject {
    param(
        [int]$EventId,
        [string]$TriggerType,
        [string]$SessionRaw,
        $IsVerifiedDisconnect
    )
    return [ordered]@{
        type                 = 'GBC_RDP_STATUS'
        schemaVersion        = 1
        computerName         = $env:COMPUTERNAME
        windowsUser          = "$env:USERDOMAIN\$env:USERNAME"
        clientName           = $env:CLIENTNAME
        collectedAt          = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        latestEventId        = $EventId
        latestRecordId       = [DateTime]::Now.Ticks
        sessionRaw           = $SessionRaw
        triggerType          = $TriggerType
        isVerifiedDisconnect = $IsVerifiedDisconnect
        events               = $null
    }
}

function Try-ReadSyncRequest {
    $text = Get-ClipboardTextSafe
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $text = $text.TrimStart()
    if (-not $text.StartsWith($SyncRequestPrefix, [StringComparison]::Ordinal)) { return $null }

    try {
        $json = $text.Substring($SyncRequestPrefix.Length).Trim()
        $obj = $json | ConvertFrom-Json
        if ($null -eq $obj -or [string]::IsNullOrWhiteSpace([string]$obj.requestId)) {
            return $null
        }
        $started = $null
        $ended = $null
        $tmp = [DateTime]::MinValue
        if (Try-ParseDateTimeUtc ([string]$obj.sessionStartedAtUtc) ([ref]$tmp)) { $started = $tmp }
        if (Try-ParseDateTimeUtc ([string]$obj.sessionEndedAtUtc) ([ref]$tmp)) { $ended = $tmp }
        return [pscustomobject]@{
            RequestId          = [string]$obj.requestId
            SessionStartedUtc  = $started
            SessionEndedUtc    = $ended
        }
    }
    catch {
        return $null
    }
}

function Set-SessionStartedMark {
    try {
        $utc = [DateTimeOffset]::UtcNow.ToString('o')
        Set-Content -LiteralPath $SessionStartedPath -Value $utc -Encoding UTF8
        Write-AgentLog ("SESSION_STARTED_MARKED | " + $utc)
    }
    catch {
        Write-AgentLog ("SESSION_STARTED_MARK_FAILED | " + $_.Exception.Message)
    }
}

function Get-SessionStartedUtc {
    try {
        if (-not (Test-Path -LiteralPath $SessionStartedPath)) { return $null }
        $text = (Get-Content -LiteralPath $SessionStartedPath -Raw).Trim()
        $dt = [DateTime]::MinValue
        if (Try-ParseDateTimeUtc $text ([ref]$dt)) { return $dt }
    }
    catch { }
    return $null
}

function Resolve-CollectWindow {
    param($SyncStartedUtc, $SyncEndedUtc)

    $toUtc = [DateTime]::UtcNow
    $fromUtc = $toUtc.AddHours(-8)
    $source = 'fallback_8h'

    if ($null -ne $SyncStartedUtc) {
        $fromUtc = [DateTime]$SyncStartedUtc
        if ($null -ne $SyncEndedUtc) { $toUtc = [DateTime]$SyncEndedUtc }
        else { $toUtc = [DateTime]::UtcNow }
        $source = 'sync_request'
    }
    else {
        $marked = Get-SessionStartedUtc
        if ($null -ne $marked) {
            $fromUtc = $marked
            $toUtc = [DateTime]::UtcNow
            $source = 'session_file'
        }
    }

    if ($toUtc -lt $fromUtc) {
        $tmp = $fromUtc
        $fromUtc = $toUtc
        $toUtc = $tmp
    }

    return [pscustomobject]@{
        FromLocal = $fromUtc.ToLocalTime().AddMinutes(-1)
        ToLocal   = $toUtc.ToLocalTime().AddMinutes(1)
        FromUtc   = $fromUtc.AddMinutes(-1)
        ToUtc     = $toUtc.AddMinutes(1)
        Source    = $source
    }
}

# ============================================================
# TFS OM collect (same approach as Collect-TfsRecent3.ps1)
# ============================================================
function Collect-TfsPayload {
    param(
        [string]$RequestId,
        [string]$DeliveryMode,
        $SyncStartedUtc,
        $SyncEndedUtc
    )

    $window = Resolve-CollectWindow -SyncStartedUtc $SyncStartedUtc -SyncEndedUtc $SyncEndedUtc
    Write-AgentLog ("TFS_COLLECT_START | mode=TFS_OM url=$CollectionUrl window=$($window.FromUtc.ToString('o'))~$($window.ToUtc.ToString('o')) source=$($window.Source)")

    $clientDll = Resolve-TfsDllPath -AssemblyName 'Microsoft.TeamFoundation.Client' -Preferred $TfsClientDllDefault
    $vcDll = Resolve-TfsDllPath -AssemblyName 'Microsoft.TeamFoundation.VersionControl.Client' -Preferred $TfsVcDllDefault

    if (-not (Test-Path -LiteralPath $clientDll)) {
        throw "TFS Client DLL not found (Team Explorer required): $clientDll"
    }
    if (-not (Test-Path -LiteralPath $vcDll)) {
        throw "TFS VersionControl DLL not found: $vcDll"
    }

    Add-Type -Path $clientDll -ErrorAction Stop
    Add-Type -Path $vcDll -ErrorAction Stop

    $tfsCollection = $null
    try {
        $tfsCollection = New-Object Microsoft.TeamFoundation.Client.TfsTeamProjectCollection ([Uri]$CollectionUrl)
        $tfsCollection.EnsureAuthenticated()

        $vcType = [Microsoft.TeamFoundation.VersionControl.Client.VersionControlServer]
        $versionControl = $tfsCollection.GetService($vcType)
        if ($null -eq $versionControl) {
            throw 'VersionControlServer service could not be loaded.'
        }

        $authorizedIdentity = $versionControl.AuthorizedIdentity
        $authorizedUserId = [string](Get-FirstProp -Object $authorizedIdentity -Names @('UniqueName', 'DisplayName'))
        $authorizedUserName = [string](Get-FirstProp -Object $authorizedIdentity -Names @('DisplayName', 'UniqueName'))
        if ([string]::IsNullOrWhiteSpace($authorizedUserId)) {
            $authorizedUserId = [string]$versionControl.AuthorizedUser
        }
        if ([string]::IsNullOrWhiteSpace($authorizedUserName)) {
            $authorizedUserName = $authorizedUserId
        }
        if ([string]::IsNullOrWhiteSpace($authorizedUserId)) {
            throw 'Authorized TFS user could not be resolved.'
        }

        Write-AgentLog ("TFS_OM_AUTH | user=$authorizedUserId")

        $queryMode = 'SESSION_WINDOW'
        $fromLocal = $window.FromLocal
        $toLocal = $window.ToLocal
        $maxCount = $TfsMaxChangesets

        $changesetItems = @(Get-OmChangesets `
                -VersionControl $versionControl `
                -UserId $authorizedUserId `
                -FromLocal $fromLocal `
                -ToLocal $toLocal `
                -MaxCount $maxCount)

        if ($changesetItems.Count -eq 0) {
            Write-AgentLog ('TFS_COLLECT_EMPTY_WINDOW | fallback=TODAY_FALLBACK')
            $queryMode = 'TODAY_FALLBACK'
            $fromLocal = [DateTime]::Today
            $toLocal = [DateTime]::Now.AddMinutes(1)
            $maxCount = $TfsFallbackTodayMax
            $changesetItems = @(Get-OmChangesets `
                    -VersionControl $versionControl `
                    -UserId $authorizedUserId `
                    -FromLocal $fromLocal `
                    -ToLocal $toLocal `
                    -MaxCount $maxCount)
            Write-AgentLog ("TFS_FALLBACK_DONE | count=$($changesetItems.Count)")
        }

        $sessionStartLocal = if ($queryMode -eq 'TODAY_FALLBACK') {
            [DateTime]::Today.ToString('yyyy-MM-dd HH:mm:ss')
        }
        else {
            $window.FromLocal.ToString('yyyy-MM-dd HH:mm:ss')
        }
        $sessionEndLocal = if ($queryMode -eq 'TODAY_FALLBACK') {
            (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        }
        else {
            $window.ToLocal.ToString('yyyy-MM-dd HH:mm:ss')
        }

        $now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        $payload = [ordered]@{
            type               = 'GBC_TFS_RECENT_CHANGESETS'
            schemaVersion      = 1
            success            = $true
            errorCode          = $null
            message            = $null
            collectionUrl      = $CollectionUrl.TrimEnd('/')
            serverPath         = $ServerPath
            queryMode          = $queryMode
            sessionStartAt     = $sessionStartLocal
            sessionEndAt       = $sessionEndLocal
            sessionToken       = $null
            remoteComputerName = $env:COMPUTERNAME
            computerName       = $env:COMPUTERNAME
            sourceClientName   = $env:CLIENTNAME
            collectedAt        = $now
            requestId          = $(if ([string]::IsNullOrWhiteSpace($RequestId)) { $null } else { $RequestId })
            deliveryId         = [Guid]::NewGuid().ToString('N')
            deliveryMode       = $(if ([string]::IsNullOrWhiteSpace($DeliveryMode)) { 'DISCONNECT' } else { $DeliveryMode })
            deliverySentAt     = $now
            authorizedUserId   = $authorizedUserId
            returnedItemCount  = $changesetItems.Count
            changesets         = $changesetItems
        }

        Write-AgentLog ("TFS_COLLECT_DONE | count=$($changesetItems.Count) mode=$queryMode author=$authorizedUserId")
        return (ConvertTo-CompactJson $payload)
    }
    finally {
        if ($null -ne $tfsCollection) {
            try { $tfsCollection.Dispose() } catch { }
        }
    }
}

function Get-OmChangesets {
    param(
        $VersionControl,
        [string]$UserId,
        [DateTime]$FromLocal,
        [DateTime]$ToLocal,
        [int]$MaxCount
    )

    $latest = [Microsoft.TeamFoundation.VersionControl.Client.VersionSpec]::Latest
    $fromSpec = New-Object Microsoft.TeamFoundation.VersionControl.Client.DateVersionSpec ($FromLocal)
    $toSpec = New-Object Microsoft.TeamFoundation.VersionControl.Client.DateVersionSpec ($ToLocal)
    $recursion = [Microsoft.TeamFoundation.VersionControl.Client.RecursionType]::Full

    $history = $null
    foreach ($path in @($ServerPath, '$/')) {
        try {
            $history = @(
                $VersionControl.QueryHistory(
                    $path,
                    $latest,
                    0,
                    $recursion,
                    $UserId,
                    $fromSpec,
                    $toSpec,
                    $MaxCount,
                    $true,
                    $false
                )
            )
            Write-AgentLog ("TFS_OM_QUERY_OK | path=$path count=$($history.Count)")
            break
        }
        catch {
            Write-AgentLog ("TFS_OM_QUERY_PATH_FAIL | path=$path err=$($_.Exception.Message)")
            $history = $null
        }
    }

    if ($null -eq $history) { return @() }

    $items = @()
    foreach ($changeset in $history) {
        if ($null -eq $changeset) { continue }
        if ($items.Count -ge $MaxCount) { break }

        $authorId = [string](Get-FirstProp -Object $changeset -Names @('Owner', 'Committer'))
        $authorName = [string](Get-FirstProp -Object $changeset -Names @('OwnerDisplayName', 'CommitterDisplayName', 'Owner', 'Committer'))
        $creationDate = $changeset.CreationDate

        $changedFiles = @()
        foreach ($change in @($changeset.Changes)) {
            if ($null -eq $change -or $null -eq $change.Item) { continue }
            $item = $change.Item
            $serverItem = [string](Get-FirstProp -Object $item -Names @('ServerItem', 'LocalItem'))
            if ([string]::IsNullOrWhiteSpace($serverItem)) { continue }
            $fileName = [System.IO.Path]::GetFileName($serverItem.Replace('/', '\'))
            $itemType = [string]$item.ItemType
            $isFolder = $itemType -match 'Folder'
            $version = Get-FirstProp -Object $item -Names @('ChangesetId', 'Version')
            if ($null -eq $version) { $version = $changeset.ChangesetId }

            $changedFiles += [ordered]@{
                changeType = (Normalize-ChangeType ([string]$change.ChangeType))
                itemType   = $(if ($isFolder) { 'folder' } else { 'file' })
                fileName   = $fileName
                path       = $serverItem
                version    = [string]$version
            }
        }

        $items += [ordered]@{
            changesetId      = [int]$changeset.ChangesetId
            authorId         = $authorId
            authorName       = $authorName
            checkedInAt      = $creationDate.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')
            comment          = [string]$changeset.Comment
            changedFileCount = $changedFiles.Count
            changedFiles     = $changedFiles
        }
    }
    return $items
}

function Update-PendingForReconnect {
    param([string]$TfsJson, [string]$RequestId)
    if ([string]::IsNullOrWhiteSpace($TfsJson)) { return $TfsJson }
    try {
        $obj = $TfsJson | ConvertFrom-Json
        $obj | Add-Member -NotePropertyName requestId -NotePropertyValue $RequestId -Force
        $obj | Add-Member -NotePropertyName deliveryMode -NotePropertyValue 'PENDING_RECONNECT' -Force
        $now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        $obj | Add-Member -NotePropertyName deliverySentAt -NotePropertyValue $now -Force
        $obj | Add-Member -NotePropertyName collectedAt -NotePropertyValue $now -Force
        if ([string]::IsNullOrWhiteSpace([string]$obj.deliveryId)) {
            $obj | Add-Member -NotePropertyName deliveryId -NotePropertyValue ([Guid]::NewGuid().ToString('N')) -Force
        }
        return (ConvertTo-CompactJson $obj)
    }
    catch {
        return $TfsJson
    }
}

# ============================================================
# Actions
# ============================================================
function Invoke-Connect {
    $hasPending = Test-Path -LiteralPath $PendingPath

    $sync = $null
    for ($i = 0; $i -lt 10 -and $null -eq $sync; $i++) {
        $sync = Try-ReadSyncRequest
        if ($null -eq $sync) { Start-Sleep -Milliseconds 300 }
    }

    $status = New-RdpStatusObject -EventId 21 -TriggerType 'AURORA_CONNECT' `
        -SessionRaw 'rdp-tcp#0 Active' -IsVerifiedDisconnect $null
    $statusJson = ConvertTo-CompactJson $status
    Write-JsonFile -Value $status -Path $LastStatusPath
    [void](Send-ClipboardPayload -Text ($RdpPrefix + $statusJson))

    # Normal connect: status only, then exit. No SYNC wait.
    # TFS fetch: SYNC must already be on clipboard before this script runs (local writes it pre-RDP).
    if ($null -eq $sync) {
        $sync = Try-ReadSyncRequest
    }

    if ($null -eq $sync) {
        Set-SessionStartedMark
        Write-AgentLog ("CONNECT_DEFAULT | no SYNC_REQUEST | status sent | exit")
        return
    }

    Write-AgentLog ("CONNECT_SYNC_REQUEST_OK | requestId=$($sync.RequestId) pending=$(Test-Path -LiteralPath $PendingPath)")
    Start-Sleep -Seconds 1

    $tfsJson = $null
    $source = $null
    if (Test-Path -LiteralPath $PendingPath) {
        try {
            $tfsJson = Get-Content -LiteralPath $PendingPath -Raw -Encoding UTF8
            $source = 'pending_file'
        }
        catch {
            Write-AgentLog ("PENDING_READ_FAILED | " + $_.Exception.Message)
        }
    }

    if ([string]::IsNullOrWhiteSpace($tfsJson)) {
        try {
            $tfsJson = Collect-TfsPayload `
                -RequestId $sync.RequestId `
                -DeliveryMode 'PENDING_RECONNECT' `
                -SyncStartedUtc $sync.SessionStartedUtc `
                -SyncEndedUtc $sync.SessionEndedUtc
            $source = 'fresh_collect'
        }
        catch {
            Write-AgentLog ("CONNECT_TFS_FAILED | " + $_.Exception.Message)
            Write-AgentError $_
            return
        }
    }
    else {
        $tfsJson = Update-PendingForReconnect -TfsJson $tfsJson -RequestId $sync.RequestId
    }

    $copied = Send-ClipboardPayload -Text ($TfsPrefix + $tfsJson) -IsTfs
    Write-AgentLog ("CONNECT_TFS_SENT | source=$source requestId=$($sync.RequestId) clipboardOk=$copied length=$($tfsJson.Length)")
    if ($copied -and (Test-Path -LiteralPath $PendingPath)) {
        Remove-Item -LiteralPath $PendingPath -Force -ErrorAction SilentlyContinue
    }
    Set-SessionStartedMark
}

function Invoke-Disconnect {
    # Main path is connect + SYNC. Disconnect trigger is optional.
    Write-AgentLog 'DISCONNECT_SKIP | no-op (use connect + SYNC_REQUEST to fetch TFS)'
}

# ============================================================
# Main
# ============================================================
try {
    $userName = "$env:USERDOMAIN\$env:USERNAME"
    Write-AgentLog ("$userName | $env:COMPUTERNAME | $Action")

    switch ($Action.ToLowerInvariant()) {
        'connect' { Invoke-Connect }
        'disconnect' { Invoke-Disconnect }
        default {
            $line = '{0} | {1} | {2} | {3}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $userName, $env:COMPUTERNAME, $Action
            [void](Send-ClipboardPayload -Text $line)
        }
    }
    exit 0
}
catch {
    Write-AgentLog ("FATAL | " + $_.Exception.Message)
    Write-AgentError $_
    exit 1
}
