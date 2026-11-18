# ScreenCoverLock

[中文](./README.md) | [English](./README_EN.md)

---

## Overview

**ScreenCoverLock** is a Windows full-screen desktop cover lock and intrusion-monitoring utility built for **"background tasks + public-space tamper protection + safe mobile/remote AI takeover"**:

- **Protection in Public Places (Dormitory, Library, Lab, Office)**:
  Keep background jobs (downloads, builds, training runs) alive with display/system wake locks while covering all monitors and blocking casual tampering.
- **Mobile / Remote AI Handover (Codex & Computer-Use Agents)**:
  When you trigger an AI agent from your phone to inspect the screen or operate your PC remotely, the agent can request a temporary pause through an owner-authorized local pipe, perform its task, and restore the cover afterward.
- **Instant Physical-Touch Re-Lock + Optional Camera Capture**:
  If anyone physically touches the keyboard or mouse while the cover is paused (or enters wrong characters while locked), **ScreenCoverLock** intercepts the input, snaps the full-screen lock back immediately, and optionally triggers local-only webcam recording.

---

## Shortcut Handling: Blocked vs. Unblocked

**Strict Mode** and **Casual Mode** apply different keyboard-filtering rules, and certain Windows kernel-level keys are intentionally or architecturally **not blocked**:

| Shortcut / Key | Strict Mode (`ScreenCover-Strict`) | Casual Mode (`ScreenCover-Casual`) | Notes |
| :--- | :--- | :--- | :--- |
| **`Win` Keys** (`Win+D`, `Win+Tab`, `Win+E`, etc.) | **Blocked** | **Blocked** | Left/Right `Win` keys are intercepted via `WH_KEYBOARD_LL` |
| **`Alt+F4`** | **Blocked** | **Blocked** | Suppressed in `ProcessCmdKey` to prevent closing the cover |
| **`Ctrl+Shift+Esc`** (Task Manager) | **Blocked** | **Not Blocked** | Blocked in Strict mode; allowed in Casual mode |
| **`Alt+Tab` / `Alt+Esc`** (Window Switch) | **Key not blocked; focus forcibly reclaimed** | **Not Blocked** | Strict mode keeps `TopMost` and reclaims foreground every `200ms`; Casual mode allows switching windows |
| **`Ctrl+Alt+Del`** (Secure Attention Sequence) | **Never Blocked** | **Never Blocked** | Handled by Windows kernel/Winlogon; user-mode apps cannot and should not block it (retains OS recovery path) |
| **`Win+L`** (Windows System Lock) | **Not Blocked** | **Not Blocked** | Handled by Windows logon subsystem; ScreenCoverLock does not replace Windows lock screen |
| **`Ctrl+Shift+Q`** (Emergency Exit) | **Handled by App** | **Handled by App** | Registered hotkey; exits cover immediately without password (works during 30s cooldown) |
| **`Ctrl+Shift+L`** (Manual Re-Lock) | **Handled by App** | **Handled by App** | Registered when `--allow-automation` is active; restores cover immediately |

> **Important**: ScreenCoverLock is a **user-space desktop cover**, not a replacement for Windows Lock Screen (`Win+L`). Anyone who knows `Ctrl+Shift+Q` or uses `Ctrl+Alt+Del` to open Task Manager / Sign-out can still exit the app.

---

## Operating Guide (Workflow & Rules)

### 1. Initial Setup (One-Time)

1. Open Settings: run `.\out\ScreenCover-Strict.exe --settings`.
2. Set a **6-character case-sensitive owner password** (stored encrypted via current-user Windows DPAPI; never stored in plaintext).
3. *(Optional)* Customize your local wallpapers, digit icons, and input dock position in Settings.
4. *(Optional)* Enable camera recording (disabled by default): click **Find** (`查找`), select a camera, run the **~3-second manual test**, verify the framing preview, check **Enable Recording**, and click **Save**.

### 2. Launching Before Leaving Your PC

- **Standard Lock (No AI takeover needed)**:
  Launch `ScreenCover-Strict.exe` (recommended in public places) or `ScreenCover-Casual.exe` without `--allow-automation`.
- **AI Remote Takeover Enabled**:
  1. Launch with `--allow-automation` (e.g., `scripts\launch-strict.ps1`).
  2. Confirm the interactive **"Enable Automation Temporary Pause"** prompt **in person** (`Yes`). Without clicking `Yes` on every launch, background scripts cannot pause the cover.

### 3. Remote / Mobile AI Control Protocol

Once authorized at startup, an AI agent controls the cover via `scripts\ScreenCover-Control.ps1`:

1. **Check Status**: `.\scripts\ScreenCover-Control.ps1 -Action Status -Mode Strict`
2. **Pause Cover**: `.\scripts\ScreenCover-Control.ps1 -Action Start -Mode Strict`
   - Hides cover windows, starts a background `KeepAlive` lease renewer (10-minute lease intervals), and arms `InputGuard`.
3. **Physical-Touch Protection During AI Operation**:
   - Injected AI inputs pass through `InputGuard`.
   - Any **non-injected physical keyboard/mouse input** is immediately swallowed and triggers an instant full-screen re-lock (`lastRestoreReason = "physical_input"`).
   - **Rule**: Once `lastRestoreReason` is `physical_input`, the AI must **never** call `Start` again autonomously—leave the screen locked for the owner.
4. **Restore Cover**: `.\scripts\ScreenCover-Control.ps1 -Action Resume -Mode Strict`

### 4. Owner Unlock & Recovery

- **Idle Dimming**: Transitions to the dark image after `10s` (configurable) of no physical input. Mouse movement and valid password prefixes keep the screen dimmed; only an **invalid character** brightens the screen and shows the warning banner.
- **Password Entry**: Up to 6 characters, IME and paste disabled. Digits 1–5 and the 6th character show length-only icons. `Backspace` is not an error; `Escape` cancels the attempt; `Enter` unlocks; 5 wrong submissions trigger a `30s` cooldown.
- **Mistype Self-Cleanup**: If you mistype a character, backspace to fix it, and unlock within the **same attempt**, any recording containing only that attempt is automatically cancelled and deleted.

---

## Architecture & Technical Principles

1. **Display Covering & Wake Lock**: Uses `SetThreadExecutionState` to keep the system and display awake while locked without modifying Windows power plans, and `DisplayCovers` to span synchronized forms across all monitors.
2. **DPAPI + HMAC Prefix Verification (`PasswordVault` & `LockState`)**: Stores the full verifier (`SHA-256`) and per-prefix `HMAC-SHA256` digests (keyed with a random 32-byte key) inside Windows DPAPI (`DataProtectionScope.CurrentUser`) files (`legacy-verifier.dat` / `prefix.dat`).
3. **Injected vs. Physical Input Separation (`InputGuard` & `ActivityMonitor`)**: Inspects low-level hook flags (`LLMHF_INJECTED` / `0x03` for mouse, `LLKHF_INJECTED` / `0x12` for keyboard). Unflagged physical input during an automation pause is blocked (`return 1`) and queued to restore the cover.
4. **Scoped Named Pipe & Lease Watchdog (`ControlPipe` & `SessionLease`)**: Pipe name is bound to the current user SID and Windows session ID (`ScreenCover.Control.v1.<SID>.<SessionId>.<mode>`), denying network access. If the keeper process stops renewing, the cover automatically re-locks when the lease expires.
5. **Isolated Recording Worker (`ScreenCover.Recorder.exe`)**: Decoupled from UI/hook threads via `%LOCALAPPDATA%\ScreenCoverUpgrade\Queue`. Invokes `tools/ffmpeg.exe` (`dshow`, video-only H.264/Matroska `.mkv`, 1s cluster flushing) with automatic retention/size cleanup and zero cloud uploads.

---

## Customizing Interface Images

All built-in assets in `assets/` are **programmatically generated, copyright-free geometric graphics**. In `--settings`, you can customize locally (stored only in `%LOCALAPPDATA%\ScreenCoverUpgrade\settings.json`):

- **Normal Wallpaper (`NormalImage`)** & **Dimmed Wallpaper (`DarkImage`)**: Leaving `DarkImage` blank automatically generates a `28%`-brightness dimmed version of your custom `NormalImage`.
- **Input Digit Icon Folder (`DigitIconFolder`)**: Auto-matches `1.png`–`5.png` (or `digit-1.png`–`5.png`) and `6.png` / `complete.png`.
- **Single Input Icon (`DigitImage`)** & **6th-Character Icon (`CompleteImage`)**.
- **Optional Error Badge (`ErrorImage` + `ShowErrorImage`)**.
- **Input Dock Position (`InputPosition`)**: `Bottom`, `Center`, or `Top`.
- Every row supports instant preview and one-click **Clear** (`清除`) to restore built-in defaults.

---

## Build & Run

```powershell
# 1. Build Strict, Casual, Recorder, and test binaries under out/
.\build.ps1

# 2. Run headless unit tests (no cover UI or camera launched)
.\tests\out\UpgradeTests.exe
.\tests\out\ControlTests.exe

# 3. (Optional) Download official FFmpeg essentials to tools/ffmpeg.exe
.\scripts\Download-FFmpeg.ps1

# 4. (Optional) Build standalone user-scope Setup.exe under dist/
.\scripts\Build-Package.ps1 -Version 1.1.0
```

- Settings & Image Customization: `.\out\ScreenCover-Strict.exe --settings`
- Strict Mode: `.\out\ScreenCover-Strict.exe` (add `--allow-automation` to enable startup authorization prompt)
- Casual Mode: `.\out\ScreenCover-Casual.exe`

---

## Security & Legal Notice

- ScreenCoverLock is a desktop-layer cover utility and **does not replace Windows Lock Screen (`Win+L`)**.
- Runtime settings, DPAPI verifiers, logs, and recordings stay strictly local under `%LOCALAPPDATA%\ScreenCoverUpgrade`.
- Licensed under the [MIT License](./LICENSE). Optional FFmpeg binaries remain subject to `tools/FFmpeg-LICENSE.txt`.
