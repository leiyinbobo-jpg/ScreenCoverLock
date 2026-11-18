param(
    [string]$Version = '1.1.0',
    [string]$Repository = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & '.\build.ps1'
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

    & '.\tests\out\UpgradeTests.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade tests failed' }

    & '.\tests\out\ControlTests.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Control tests failed' }

    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $hasFfmpeg = Test-Path -LiteralPath '.\tools\ffmpeg.exe'
    if ($hasFfmpeg) {
        & $compiler /nologo /codepage:65001 /r:System.Core.dll /r:System.Security.dll /r:System.Web.Extensions.dll /main:CodecTest '/out:tests\out\CodecTest.exe' 'src\UpgradeCore.cs' 'src\Recorder.cs' 'tests\CodecTest.cs'
        if ($LASTEXITCODE -ne 0) { throw 'Codec test compilation failed' }
        $sample = Join-Path $root 'tests\out\codec-validation.mkv'
        & '.\tests\out\CodecTest.exe' $sample
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic codec validation failed' }
        Remove-Item -LiteralPath $sample -ErrorAction SilentlyContinue
    } else {
        Write-Warning 'tools/ffmpeg.exe not found; skipping live FFmpeg codec test. Run scripts\Download-FFmpeg.ps1 if camera recording is needed.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $build = [DateTime]::UtcNow.AddHours(8).ToString('yyyyMMdd-HHmmss')
    $label = "$Version-build.$build"
    $folder = Join-Path $root "dist\$label"
    if (Test-Path -LiteralPath $folder) { throw 'Build directory exists; refusing reuse' }
    New-Item -ItemType Directory -Path $folder | Out-Null

    $stage = Join-Path $folder 'stage'
    New-Item -ItemType Directory -Path "$stage\tools" | Out-Null

    @{ version = $Version; build = $build; label = $label } | ConvertTo-Json | Set-Content -LiteralPath '.\version.json' -Encoding UTF8

    Copy-Item -LiteralPath 'out\ScreenCover-Strict.exe', 'out\ScreenCover-Casual.exe', 'out\ScreenCover.Recorder.exe',
        'out\ScreenCover-Control.ps1', 'out\launch-strict.ps1', 'out\launch-casual.ps1', 'out\launch-settings.ps1',
        'assets\cover-background.png', 'assets\cover-dark.png', 'README.md', 'version.json' -Destination $stage -Force
    Copy-Item -LiteralPath 'tools\SOURCE.txt', 'tools\FFmpeg-LICENSE.txt', 'tools\FFmpeg-README.txt', 'tools\FFmpeg-archive.sha256' -Destination "$stage\tools" -Force
    if ($hasFfmpeg) { Copy-Item -LiteralPath 'tools\ffmpeg.exe' -Destination "$stage\tools" -Force }

    $payload = Join-Path $folder 'payload.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

    $source = [IO.File]::ReadAllText((Join-Path $root 'src\Installer.cs')).Replace('__VERSION__', $label)
    $tempSource = Join-Path $folder 'Installer.generated.cs'
    [IO.File]::WriteAllText($tempSource, $source, (New-Object Text.UTF8Encoding($true)))

    $installer = Join-Path $folder "ScreenCoverLock-$label-win-x64-Setup.exe"
    & $compiler /nologo /codepage:65001 /target:winexe /r:System.Windows.Forms.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$payload,payload.zip" "/out:$installer" $tempSource
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }

    $verify = Join-Path $folder 'verify-extracted'
    $process = Start-Process -FilePath $installer -ArgumentList @('--extract-to', ('"' + $verify + '"')) -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw 'Installer extraction validation failed' }

    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt = [ordered]@{
        application = 'ScreenCoverLock'
        version = $Version
        build = $build
        tag = "v$label"
        repository = $Repository
        file = [IO.Path]::GetFileName($installer)
        size = (Get-Item -LiteralPath $installer).Length
        sha256 = $hash
        builtUtc = [DateTime]::UtcNow.ToString('o')
        includesFfmpeg = $hasFfmpeg
        containsUserData = $false
    }
    $receiptPath = Join-Path $folder 'build-receipt.json'
    $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
    "$hash  $($receipt.file)" | Set-Content -LiteralPath (Join-Path $folder "$($receipt.file).sha256") -Encoding ASCII
    Write-Output "RECEIPT=$receiptPath"
} finally {
    Pop-Location
}
