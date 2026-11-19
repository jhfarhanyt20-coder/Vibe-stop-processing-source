# Vibe Stop Processing: PC Shutdown Assistant

A dark-themed Windows app by **Vibe** to run **right before you turn your PC off**.
Press one button and every non-system app is closed one by one. The app also cleans temporary files, fixes a PC that **turns itself back on after shutdown**, and **keeps itself up to date**.

> Windows 10 / 11 (64-bit) · standard Windows installer · runs on .NET Framework 4, which is already part of Windows

## Download
Get **`VibeStopProcessing-Setup-x.y.z.exe`** from the **[Releases page](../../releases/latest)** and run it.

The installer:
- shows the license agreement,
- installs to `C:\Program Files\Vibe\Stop Processing`,
- adds a Start Menu shortcut and, if you want one, a Desktop shortcut,
- registers an uninstaller under **Settings → Apps** (or **Control Panel → Programs**).

Running a newer installer over an existing install upgrades it in place.

---

## Features

### 1. Processes: stop everything before shutdown
- Shows running processes live, with CPU, memory and a status for each one.
- **Stop All Processes** closes apps one at a time and shows the progress. Apps with a window get a moment to close cleanly, the rest are ended.
- **Right-click** any process to stop just that one, stop all copies of it, or open its file location.
- **Windows system processes are never touched.** That covers `explorer`, `svchost`, `lsass`, Defender, drivers and anything in `C:\Windows`.
- **No questions asked:** everything you started (browsers, editors, `node`, `bun`, terminals...) is stopped one by one in a single click. Windows processes and services are never touched.
- Optional **"Shut down PC when finished"**: the PC powers off 15 seconds after the run, and you can cancel it in that time.

### 2. Cleaner: delete temp files in one click
| Location | Default |
|---|---|
| User temp files (`%TEMP%`) | ✅ |
| Windows temp files (`C:\Windows\Temp`) | ✅ |
| Thumbnail cache | ✅ |
| Crash dumps and error reports | ✅ |
| Browser cache: Chrome, Edge, Brave, Opera, Vivaldi, Firefox (logins and history are kept) | ✅ |
| Windows Update download cache | ⬜ |
| Prefetch data | ⬜ |
| Recycle Bin | ⬜ |

- **Clean Now** deletes everything in the ticked locations.
- **Files that are in use are skipped.** You are asked **once** (Skip All / Stop), then every locked file is skipped automatically. Untick *"Ask once before skipping locked files"* to never be asked.
- At the end it shows how much space was freed, how many files were deleted and how many were skipped.

### 3. Power Fix: PC turns back on after shutdown
| Cause | What the fix does |
|---|---|
| **Network card Wake-on-LAN** (incl. the hidden "Shutdown Wake-On-LAN") | Turns it off. This is the most common reason a PC switches itself back on right after shutdown |
| Fast Startup (hybrid shutdown) | Turns it off, so shutdown becomes a real full shutdown |
| Wake timers | Disables them |
| Devices allowed to wake the PC (network card / Wake-on-LAN, mouse, keyboard) | Disables wake for each device |
| Automatic maintenance wake-up | Turns it off |
| Auto sign-in / restart after updates | Turns it off |
| Restart on system failure | Turns it off, so the PC stays off if shutdown crashes |

- **Fix Auto Power-On** applies all of the above in one click. **Restore Defaults** undoes it.
- **Full Shut Down Now** does a proper full shutdown.

> **Still turning on by itself?** Open the BIOS (usually `Del` or `F2`) and set *Restore on AC Power Loss → Power Off*, *Wake on LAN → Disabled*, *RTC Alarm → Disabled*, *ErP → Enabled*.

### 4. Tools: one-click fixes
| Tool | What it does |
|---|---|
| **Free Up RAM** | Releases memory that apps are holding but not using, and shows how much was freed |
| **Repair Windows** | Runs DISM + SFC to find and repair damaged Windows system files |
| **Fix Internet** | Flushes DNS and resets Winsock, TCP/IP and the IP address |
| **Restart Explorer** | Fixes a frozen taskbar, Start menu or desktop |
| **Disk Health** | Shows whether each HDD/SSD reports *Healthy*, and warns you to back up if it doesn't |
| **Battery Report** | Opens Windows' detailed laptop battery report (capacity and wear) |

**Delete Permanently (built-in unlocker):** erases a file or folder (e.g. `node_modules`) without the Recycle Bin, after a strong warning. It escalates step by step: normal delete with `\\?\` long-path support → take ownership + grant Administrators → find the programs locking the files (Restart Manager) and offer **Close & Delete** → **Delete on Restart** (`MoveFileEx`). Drives, Windows, Program Files and the main user folders are blocked.

The **This PC** panel shows the Windows version, CPU, RAM use, uptime, and free space on every drive.

### 5. Runs quietly in the system tray
- The **✕** button hides the app to the tray (next to the clock) instead of closing it.
- **Click** the tray icon to open the app. **Right-click** it for quick links (Processes, Cleaner, Power Fix, Tools, Settings, Shut Down PC) and **Exit**.
- **Starts with Windows** in the tray. It uses a scheduled task, so there's no UAC prompt at sign-in.
- Only one copy runs at a time. Opening the app again just brings the existing window forward.
- You can turn both behaviours off in **Settings → General**.

### 6. Settings: automatic updates
- **Install updates automatically** is on by default. A few seconds after the app starts, it checks for a new version, downloads it and installs it, then asks whether to restart now.
- With auto update **off**, the app tells you a new version exists and you install it yourself with **Check for Updates → Install Update**.
- Each update is checked before it's installed: the file size, the SHA-256 checksum and whether it's a valid program. If anything fails, the old version is kept.
- Release notes for each version are shown in the app.

---

## How to use
1. Download and run `VibeStopProcessing-Setup-x.y.z.exe` from the [Releases page](../../releases/latest).
2. Open **Vibe Stop Processing** from the Start Menu and press **Yes** on the UAC prompt. The app needs administrator rights to change power settings and to clean `C:\Windows\Temp`.
3. Before turning the PC off:
   - **Cleaner** → **Clean Now**
   - **Processes** → tick *Shut down PC when finished* → **Stop All Processes**
4. If your PC wakes up on its own: **Power Fix** → **Fix Auto Power-On**. You only need to do this once.

---

## For the publisher: where things live
| Repository | Visibility | Contents |
|---|---|---|
| `Vibe-stop-processing-source` | **Private** | All source code and history (this repo) |
| `Vibe-stop-processing` | Public | README, LICENSE and releases only. Users download from here and the app updates from here |

The public repo's README lives in `public/README.md`. Push changes to it by hand when needed.

## For the publisher: releasing an update
The version goes up by **0.1 automatically** on every release (2.1.0 → 2.2.0 → 2.3.0). One command builds the app and the installer, pushes the code and creates the GitHub release:

```powershell
.\release.ps1 -Notes "Added X, fixed Y"
```

Each release contains three files:

| File | Used by |
|---|---|
| `VibeStopProcessing-Setup-x.y.z.exe` | New users (installer) |
| `StopProcessing.exe` | The in-app updater |
| `latest.json` | The in-app updater: version, notes, size and SHA-256 |

To build locally without publishing anything, run `.\release.ps1 -BuildOnly`.
Requirements: Git, GitHub CLI (`gh`) signed in to the repository owner's account, and Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

---

## Safety notes
- System processes and anything inside the Windows folder are protected from **Stop All**.
- The Cleaner only deletes inside the listed temp/cache folders and never follows junctions or symlinks.
- Every Power Fix change can be undone with **Restore Defaults**.
- Deleted temp files and an emptied Recycle Bin **cannot be recovered**.

## License
**Proprietary. © 2026 Vibe. All rights reserved.**
You may download and use the official release. You may not modify, reverse-engineer, redistribute or resell it. See [LICENSE](LICENSE).
