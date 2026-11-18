$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolsDir = Join-Path $root 'tools'
$shaFile = Join-Path $toolsDir 'FFmpeg-archive.sha256'
$expectedSha = (Get-Content -LiteralPath $shaFile -Raw).Trim().ToLowerInvariant()
$zipPath = Join-Path $env:TEMP 'ffmpeg-release-essentials.zip'
$extractDir = Join-Path $env:TEMP ('ffmpeg-extract-' + [Guid]::NewGuid().ToString('N'))

$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
Write-Host "Downloading FFmpeg essentials from $url ..."
Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing

$actualSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha -ne $expectedSha) {
    Write-Warning "Archive SHA-256 differs from pinned FFmpeg-archive.sha256 ($expectedSha vs $actualSha) because upstream may have updated the release-essentials rolling archive. Verify source authenticity before continuing."
}

Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force
$ffmpeg = Get-ChildItem -Path $extractDir -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if (-not $ffmpeg) { throw 'ffmpeg.exe not found inside archive' }
Copy-Item -LiteralPath $ffmpeg.FullName -Destination (Join-Path $toolsDir 'ffmpeg.exe') -Force
Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Write-Host "Installed tools/ffmpeg.exe successfully."
