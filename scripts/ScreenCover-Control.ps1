[CmdletBinding()]
param(
    [ValidateSet('Status', 'Start', 'Renew', 'Resume', 'KeepAlive')]
    [string]$Action = 'Status',
    [ValidateSet('Strict', 'Casual')]
    [string]$Mode = 'Strict',
    [ValidateRange(1, 10)]
    [int]$LeaseMinutes = 10,
    [ValidateRange(0, 30)]
    [int]$MaxSessionMinutes = 0,
    [string]$SessionFile,
    # Advanced override for a headless test fixture; always uses local server '.'.
    [ValidatePattern('^[A-Za-z0-9_.-]{1,180}$')]
    [string]$PipeName
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
if (-not $PipeName) {
    $desktopSession = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    $PipeName = 'ScreenCover.Control.v1.' + $userSid.Value + '.' + $desktopSession + '.' + $Mode.ToLowerInvariant()
}
if (-not $SessionFile) {
    $SessionFile = Join-Path $env:LOCALAPPDATA ('ScreenCoverControl/session-' + $Mode.ToLowerInvariant() + '.json')
}
$SessionFile = [System.IO.Path]::GetFullPath($SessionFile)

function Invoke-Control([hashtable]$Request) {
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $PipeName,
        [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
    $reader = $null
    $writer = $null
    try {
        try { $pipe.Connect(4000) }
        catch { throw 'NO_SERVER: 未找到可控制的 ScreenCoverLock 实例。' }
        $encoding = [System.Text.UTF8Encoding]::new($false)
        $writer = [System.IO.StreamWriter]::new($pipe, $encoding, 1024, $true)
        $reader = [System.IO.StreamReader]::new($pipe, $encoding, $false, 1024, $true)
        $writer.AutoFlush = $true
        $writer.WriteLine(($Request | ConvertTo-Json -Compress -Depth 4))
        $read = $reader.ReadLineAsync()
        if (-not $read.Wait(6000)) { throw 'TIMEOUT: 控制请求超时，未确认操作结果。' }
        if (-not $read.Result -or $read.Result.Length -gt 8192) { throw 'BAD_RESPONSE: 控制接口响应无效。' }
        $reply = $read.Result | ConvertFrom-Json
        return $reply
    } finally {
        if ($null -ne $reader) { $reader.Dispose() }
        if ($null -ne $writer) { $writer.Dispose() }
        $pipe.Dispose()
    }
}
function Assert-Reply($Reply) {
    if (-not $Reply.ok) { throw ($Reply.error + ': ' + $Reply.message) }
}
function Read-Session {
    if (-not [System.IO.File]::Exists($SessionFile)) {
        throw 'NO_SESSION_FILE: 没有当前会话文件。可用 Ctrl+Shift+L 手动恢复遮屏。'
    }
    $state = [System.IO.File]::ReadAllText($SessionFile) | ConvertFrom-Json
    if (-not $state.Session -or $state.PipeName -ne $PipeName) { throw 'INVALID_STATE: 会话文件与当前模式不匹配。' }
    return $state
}
function Write-Session($State) {
    $directory = [System.IO.Path]::GetDirectoryName($SessionFile)
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    if ((Get-Item -LiteralPath $directory -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw 'INVALID_STATE_DIR: 不使用重解析目录存放会话凭据。'
    }
    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    # Change only the DACL; retain the existing owner.
    $inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $propagation = [System.Security.AccessControl.PropagationFlags]::None
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($userSid,
        [System.Security.AccessControl.FileSystemRights]::FullControl, $inherit, $propagation,
        [System.Security.AccessControl.AccessControlType]::Allow))
    [System.IO.Directory]::SetAccessControl($directory, $acl)
    $tempFile = $SessionFile + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [System.IO.File]::WriteAllText($tempFile, ($State | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
        if ([System.IO.File]::Exists($SessionFile)) { [System.IO.File]::Replace($tempFile, $SessionFile, ($tempFile + '.bak')); [System.IO.File]::Delete($tempFile + '.bak') }
        else { [System.IO.File]::Move($tempFile, $SessionFile) }
    } finally { if ([System.IO.File]::Exists($tempFile)) { [System.IO.File]::Delete($tempFile) } }
}
function Remove-OwnSession([string]$Token) {
    try {
        if ([System.IO.File]::Exists($SessionFile)) {
            $current = [System.IO.File]::ReadAllText($SessionFile) | ConvertFrom-Json
            if ($current.Session -eq $Token) { [System.IO.File]::Delete($SessionFile) }
        }
    } catch { }
}

try {
    switch ($Action) {
        'Status' {
            $reply = Invoke-Control @{ action = 'status' }; Assert-Reply $reply
            $reply | ConvertTo-Json -Compress
        }
        'Start' {
            $reply = Invoke-Control @{ action = 'pause'; leaseSeconds = ($LeaseMinutes * 60); maxSeconds = ($MaxSessionMinutes * 60) }
            Assert-Reply $reply
            $token = [string]$reply.session
            try {
                $state = [pscustomobject]@{ Session = $token; Instance = $reply.instance; PipeName = $PipeName;
                    Mode = $Mode; CreatedUtc = [DateTime]::UtcNow.ToString('o'); KeeperPid = 0 }
                Write-Session $state
                $hostExecutable = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
                $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
                    '-File', ('"' + $PSCommandPath + '"'), '-Action', 'KeepAlive', '-Mode', $Mode,
                    '-LeaseMinutes', $LeaseMinutes, '-MaxSessionMinutes', $MaxSessionMinutes,
                    '-SessionFile', ('"' + $SessionFile + '"'), '-PipeName', $PipeName)
                $keeper = Start-Process -FilePath $hostExecutable -ArgumentList $arguments -WindowStyle Hidden -PassThru
                $state.KeeperPid = $keeper.Id
                Write-Session $state
                Start-Sleep -Milliseconds 500
                $keeper.Refresh()
                if ($keeper.HasExited) { throw 'KEEPER_FAILED: 续期进程未能持续运行。' }
                # The bearer token is intentionally never written to console/logs.
                [pscustomobject]@{ ok = $true; state = 'paused'; mode = $Mode; KeeperPid = $keeper.Id;
                    LeaseMinutes = $LeaseMinutes; MaxSessionMinutes = $MaxSessionMinutes; UnlimitedSession = ($MaxSessionMinutes -eq 0); SessionFile = $SessionFile } | ConvertTo-Json -Compress
            } catch {
                try { Invoke-Control @{ action = 'resume'; session = $token } | Out-Null } catch { }
                Remove-OwnSession $token
                throw
            }
        }
        'Renew' {
            $state = Read-Session
            $reply = Invoke-Control @{ action = 'renew'; session = [string]$state.Session; leaseSeconds = ($LeaseMinutes * 60) }
            Assert-Reply $reply
            $reply | ConvertTo-Json -Compress
        }
        'Resume' {
            $state = Read-Session
            $reply = Invoke-Control @{ action = 'resume'; session = [string]$state.Session }
            if (-not $reply.ok -and $reply.error -ne 'NO_SESSION') { Assert-Reply $reply }
            Remove-OwnSession ([string]$state.Session)
            [pscustomobject]@{ ok = $true; state = 'covered'; mode = $Mode } | ConvertTo-Json -Compress
        }
        'KeepAlive' {
            $state = Read-Session
            $token = [string]$state.Session
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            $nextRenewal = 0.0
            try {
                while ($MaxSessionMinutes -eq 0 -or $watch.Elapsed.TotalSeconds -lt ($MaxSessionMinutes * 60)) {
                    # A manual Resume removes this file. Never recreate a lease.
                    if (-not [System.IO.File]::Exists($SessionFile)) { break }
                    if ($watch.Elapsed.TotalSeconds -ge $nextRenewal) {
                        $reply = Invoke-Control @{ action = 'renew'; session = $token; leaseSeconds = ($LeaseMinutes * 60) }
                        if (-not $reply.ok -or $reply.state -ne 'paused') { break }
                        $nextRenewal = $watch.Elapsed.TotalSeconds + 30
                    }
                    Start-Sleep -Milliseconds 500
                }
            } finally {
                try { Invoke-Control @{ action = 'resume'; session = $token } | Out-Null } catch { }
                Remove-OwnSession $token
            }
        }
    }
} catch {
    # Error messages contain no session credentials.
    Write-Error $_.Exception.Message
    exit 1
}
