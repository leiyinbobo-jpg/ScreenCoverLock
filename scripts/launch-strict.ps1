$ErrorActionPreference = 'Stop'
Start-Process -FilePath (Join-Path $PSScriptRoot 'ScreenCover-Strict.exe') -ArgumentList '--allow-automation'
