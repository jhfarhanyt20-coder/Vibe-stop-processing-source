# Vibe Stop Processing: PC Shutdown Assistant

A dark-themed Windows app by **Vibe** to run **right before you turn your PC off**.
Press one button and every non-system app is closed one by one. The app also cleans temporary files, fixes a PC that **turns itself back on after shutdown**, includes one-click repair tools, and **keeps itself up to date**.

> Windows 10 / 11 (64-bit) · standard Windows installer · runs on .NET Framework 4, which is already part of Windows

## ⬇️ Download
Get **`VibeStopProcessing-Setup-x.y.z.exe`** from the **[latest release](../../releases/latest)** and run it.

The installer:
- shows the license agreement,
- installs to `C:\Program Files\Vibe\Stop Processing`,
- adds a Start Menu shortcut and, if you want one, a Desktop shortcut,
- registers an uninstaller under **Settings → Apps** (or **Control Panel → Programs**).

Once it's installed, the app updates itself. You don't need to download new versions.

---

## Features

### 1. Processes: stop everything before shutdown
- Shows running processes live, with CPU, memory and a status for each one.
- **Stop All Processes** closes apps one at a time and shows the progress.
- **Windows system processes are never touched.**
- Important apps such as browsers, Office, Discord and Steam **ask before closing**, so you don't lose unsaved work.
- Optional **"Shut down PC when finished"**, with a 15-second cancel window.

### 2. Cleaner: delete junk files in one click
Cleans user and Windows temp files, the thumbnail cache, crash dumps, **browser cache** (Chrome, Edge, Brave, Opera, Vivaldi and Firefox, with logins and history kept), and optionally the Windows Update cache, Prefetch data and the Recycle Bin.
Files that are in use are skipped: you choose **Skip / Skip All / Stop**. At the end it shows how much space was freed.

### 3. Power Fix: PC turns back on after shutdown
One click switches off everything in Windows that can power your PC back on: Fast Startup, wake timers, devices allowed to wake the PC (network card, mouse, keyboard), maintenance wake-up, auto sign-in after updates, and restart on system failure.
**Restore Defaults** undoes all of it.

### 4. Tools: one-click fixes
| Tool | What it does |
|---|---|
| **Free Up RAM** | Releases memory that apps are holding but not using |
| **Repair Windows** | Finds and repairs damaged Windows system files |
| **Fix Internet** | Resets DNS, Winsock and TCP/IP when the internet stops working |
| **Restart Explorer** | Fixes a frozen taskbar, Start menu or desktop |
| **Disk Health** | Shows whether each HDD/SSD is healthy |
| **Battery Report** | Shows your laptop battery's real capacity and wear |

There is also a **This PC** panel with your Windows version, CPU, RAM, uptime and free space on every drive.

### 5. Runs quietly in the system tray
- The **✕** button hides the app to the tray. **Right-click** the tray icon and choose **Exit** to close it.
- **Starts with Windows** in the tray, with no UAC prompt.
- You can turn both behaviours off in **Settings → General**.

### 6. Automatic updates
- On by default. New versions download and install themselves, and every update is checked with a SHA-256 checksum before it's installed.
- Prefer to choose for yourself? Turn auto update off and use **Settings → Check for Updates**.

---

## Requirements
- Windows 10 or 11, 64-bit
- Administrator rights. The app asks once when it starts, because changing power settings and cleaning system folders need them.

## Support
Found a problem or have an idea? Open an **[issue](../../issues)**.

## License
**Proprietary. © 2026 Vibe. All rights reserved.**
You may download and use the official release. You may not modify, reverse-engineer, redistribute or resell it. See [LICENSE](LICENSE).
