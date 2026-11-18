$ErrorActionPreference = 'Stop'
Start-Process -FilePath (Join-Path $PSScriptRoot 'ScreenCover-Casual.exe') -ArgumentList '--allow-automation'
