$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) {
        throw "C# compiler not found: $compiler"
    }

    New-Item -ItemType Directory -Path 'out', 'tests\out' -Force | Out-Null

    $refs = @(
        '/nologo', '/codepage:65001',
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
        '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.Security.dll'
    )

    $resourceNames = @('cover-background.png', 'cover-dark.png', 'digit-complete.png', 'error-badge.png')
    $resourceNames += 1..5 | ForEach-Object { "digit-$_.png" }
    $resourceArgs = $resourceNames | ForEach-Object {
        $p = Join-Path $PSScriptRoot "assets\$_"
        if (-not (Test-Path -LiteralPath $p)) { throw "Missing asset: $p" }
        "/resource:$p,$_"
    }

    $shared = @(
        'src\CoverBackground.cs', 'src\DisplayCovers.cs', 'src\ControlCore.cs',
        'src\ControlPipe.cs', 'src\UiControlHost.cs', 'src\InputGuard.cs',
        'src\UpgradeCore.cs', 'src\ActivityMonitor.cs', 'src\SettingsForm.cs', 'src\Recorder.cs'
    )

    & $compiler @refs @resourceArgs '/target:winexe' '/main:ScreenCoverStrict.Program' '/win32icon:assets\icon-strict.ico' '/out:out\ScreenCover-Strict.exe' 'src\ScreenCoverStrict.cs' 'src\AutomationForm.strict.cs' 'src\UpgradeForm.strict.cs' @shared
    if ($LASTEXITCODE -ne 0) { throw 'Strict build failed' }

    & $compiler @refs @resourceArgs '/target:winexe' '/main:ScreenCoverCasual.Program' '/win32icon:assets\icon-casual.ico' '/out:out\ScreenCover-Casual.exe' 'src\ScreenCoverCasual.cs' 'src\AutomationForm.casual.cs' 'src\UpgradeForm.casual.cs' @shared
    if ($LASTEXITCODE -ne 0) { throw 'Casual build failed' }

    & $compiler @refs '/target:winexe' '/main:ScreenCoverUpgrade.RecorderProgram' '/out:out\ScreenCover.Recorder.exe' 'src\UpgradeCore.cs' 'src\Recorder.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Recorder build failed' }

    & $compiler @refs @resourceArgs '/target:exe' '/out:tests\out\UpgradeTests.exe' 'src\UpgradeCore.cs' 'src\CoverBackground.cs' 'src\ControlCore.cs' 'src\InputGuard.cs' 'tests\UpgradeTests.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade tests build failed' }

    & $compiler /nologo /codepage:65001 /r:System.Core.dll /r:System.Web.Extensions.dll /target:exe '/out:tests\out\ControlTests.exe' 'src\ControlCore.cs' 'src\ControlPipe.cs' 'src\InputGuard.cs' 'tests\Tests.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Control tests build failed' }

    Copy-Item -LiteralPath 'assets\cover-background.png', 'assets\cover-dark.png' -Destination 'out\' -Force
    Copy-Item -LiteralPath 'scripts\ScreenCover-Control.ps1', 'scripts\launch-strict.ps1', 'scripts\launch-casual.ps1', 'scripts\launch-settings.ps1' -Destination 'out\' -Force

    Write-Output 'Built ScreenCover-Strict.exe, ScreenCover-Casual.exe, ScreenCover.Recorder.exe and test binaries under out/.'
} finally {
    Pop-Location
}
