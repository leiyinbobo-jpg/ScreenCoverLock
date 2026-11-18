# ScreenCoverLock

[中文](./README.md) | [English](./README_EN.md)

---

## Overview

**ScreenCoverLock** is a lightweight, local-first Windows desktop screen-cover lock utility built with .NET Framework (C# / WinForms).  
It provides customizable full-screen lock wallpapers, idle screen dimming, per-character prefix error detection, optional local-only camera recording on intrusion attempts, and an owner-authorized automation pause interface guarded against physical input.

---

## Key Features

- **Dual Lock Modes**
  - **Strict Mode (`ScreenCover-Strict.exe`)**: Topmost full-screen cover that actively retains foreground focus and blocks `Win`, `Alt+F4`, and `Ctrl+Shift+Esc`.
  - **Casual Mode (`ScreenCover-Casual.exe`)**: Lightweight full-screen cover without aggressive focus stealing.
- **Multi-Monitor Support**
  - Automatically tracks display configuration changes and spans synchronized cover windows across all connected monitors.
- **Idle Screen Dimming**
  - Transitions to a dimmed wallpaper after a configurable period without physical keyboard/mouse activity (default: `10s`).
  - Remains dimmed on mouse movement or valid password prefixes; brightens and shows an alert only when an invalid character is typed.
  - Does not alter Windows hardware/system display brightness.
- **Fully Customizable UI Images (Copyright-Free Defaults)**
  - Every built-in wallpaper, digit indicator, warning badge, and `.ico` application icon bundled in this repository is a clean, **programmatically generated geometric asset** with zero third-party copyrighted art.
  - Users can replace UI images locally via the Settings UI without ever committing personal or copyrighted media to the repository.

---

## Customizing Interface Images

Launch the settings UI with `--settings` and verify your owner password to customize:

1. **Normal Wallpaper (`NormalImage`)**: Full-screen image shown when active or on input errors (aspect-fill scaling).
2. **Dimmed Wallpaper (`DarkImage`)**: Shown when idle.
   - **Auto-Dim Fallback**: If you set a custom `NormalImage` and leave `DarkImage` blank, ScreenCoverLock automatically generates a dimmed version (`28%` brightness) of your custom wallpaper.
3. **Password Input Indicators (Digits 1–5 & 6th-Character Icon)**:
   - **Icon Folder (`DigitIconFolder`)**: Select a local folder containing `1.png`–`5.png` (or `digit-1.png`–`digit-5.png`).
   - **Single Input Icon (`DigitImage`)**: Optional single image used for digits 1–5 when a folder or specific digit file is not provided.
   - **6th-Character Icon (`CompleteImage`)**: Shown when 6 characters are entered (also auto-detects `6.png` or `complete.png` inside `DigitIconFolder`).
   - *Note: Indicators only reflect input length and never reveal whether individual characters are correct.*
4. **Optional Error Badge (`ErrorImage` + `ShowErrorImage`)**: Toggle and customize an image displayed alongside the warning banner on invalid input.
5. **Input Dock Position (`InputPosition`)**: Choose `Bottom` (default), `Center`, or `Top` to fit your wallpaper composition.
6. **Live Preview & One-Click Reset**: Preview any image directly inside Settings, or click **Clear** (`清除`) to revert to the built-in default assets.

---

## Password Security & Input Rules

- **First-Time Setup**: Set a 6-character owner password locally on first launch.
- **DPAPI + HMAC Prefix Verification**:
  - Password verifier and per-prefix `HMAC-SHA256` checks are protected using the current Windows user's **DPAPI** (`DataProtectionScope.CurrentUser`).
  - No plaintext password or plaintext prefixes are written to disk.
- **Anti-Brute-Force & Ergonomics**:
  - Case-sensitive; IME and clipboard paste shortcuts are disabled.
  - Backspace does not count as an error; modifier-only keys do not trigger errors.
  - Incomplete attempts clear automatically after `60s` of inactivity (configurable); `Escape` clears the current attempt manually.
  - 5 wrong submissions trigger a `30s` cooldown.
  - Press `Enter` with the full password to unlock, or `Ctrl+Shift+Q` for emergency exit.

---

## Optional Local Camera Recording (Disabled by Default)

- **Opt-In Only**: Camera recording is disabled by default. The owner must explicitly select a camera, run a ~3-second test capture, confirm the framing, and save the setting.
- **Triggers**: Invalid password characters, physical `Ctrl+Shift+Q` emergency exit, or optional deliberate mouse movement (off by default).
- **Isolated Recorder Worker**:
  - `ScreenCover.Recorder.exe` invokes `tools/ffmpeg.exe` to record video-only (no audio) H.264 `.mkv` clips locally.
  - If a mistyped attempt is corrected via backspace and unlocked successfully within the same attempt, that single-attempt clip is automatically deleted.
  - Built-in Event Viewer lets you inspect triggers, play clips, delete entries, or rely on automatic retention/size cleanup.
  - **No cloud upload** or network transmission of any kind.

---

## Optional Automation Pause Control

Designed for local workflows that need temporary screen access only when explicitly authorized at startup:

- Requires passing `--allow-automation` AND confirming the interactive owner prompt on launch.
- Controlled via a local-only Named Pipe (`scripts/ScreenCover-Control.ps1`) restricted to the current Windows user SID and desktop session.
- **Physical Input Guard (`InputGuard`)**: Low-level hooks monitor for non-injected physical keyboard/mouse input during a pause and immediately restore the full-screen cover if physical interaction occurs.
- Press `Ctrl+Shift+L` anytime to manually restore the cover immediately.

---

## Build & Run

### Prerequisites

- Windows 10 / 11 (x64)
- Built-in .NET Framework 4.x C# compiler (`csc.exe`)
- PowerShell 5.1+

### Build & Test

```powershell
# 1. Compile Strict, Casual, Recorder, and test executables
.\build.ps1

# 2. Run headless state, image-customization, and control-lease tests
.\tests\out\UpgradeTests.exe
.\tests\out\ControlTests.exe

# 3. (Optional) Download official FFmpeg essentials to tools/ffmpeg.exe for camera recording
.\scripts\Download-FFmpeg.ps1

# 4. (Optional) Build standalone user-scope Setup.exe under dist/
.\scripts\Build-Package.ps1 -Version 1.1.0
```

### Launch

- Open Settings & Image Customization: `.\out\ScreenCover-Strict.exe --settings`
- Run Strict Mode: `.\out\ScreenCover-Strict.exe`
- Run Casual Mode: `.\out\ScreenCover-Casual.exe`
- Add `--allow-automation` to enable the startup authorization prompt for local pipe control.

---

## Repository Structure

```text
ScreenCoverLock/
├─ assets/          # Built-in copyright-free geometric wallpapers, input icons, and .ico files
├─ src/             # C# source files (cover windows, settings UI, multi-display, pipe control, recorder, installer)
├─ scripts/         # Packaging, automation control, launch helpers, and FFmpeg downloader
├─ tests/           # Headless unit tests for lock state, custom images, control leases, and named pipes
├─ tools/           # FFmpeg upstream metadata, SHA-256 reference, and license files (no binaries)
├─ build.ps1        # Build script using built-in .NET Framework csc.exe
├─ LICENSE          # MIT License
├─ README.md        # Chinese documentation
└─ README_EN.md     # English documentation
```

---

## Security & Legal Notice

- **Not a Windows Lock Screen Replacement**: ScreenCoverLock is a user-space desktop cover utility and does **not** replace `Win+L` or Windows logon security.
- **Local Data Isolation**: User settings, DPAPI password verifiers, event logs, and recordings live under `%LOCALAPPDATA%\ScreenCoverUpgrade` with user-scoped ACLs. Never commit personal runtime directories to version control.
- **Copyright**: All default visual assets in this repository are original geometric graphics. Ensure you hold appropriate rights for any custom images you select locally.
- Licensed under the [MIT License](./LICENSE). Optional FFmpeg binaries remain subject to their upstream licenses (`tools/FFmpeg-LICENSE.txt`).
