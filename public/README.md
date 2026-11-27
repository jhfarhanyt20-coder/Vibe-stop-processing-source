# BD All-in-One Tools: PC Shutdown Assistant

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
- **Stop All Processes** closes apps one at a time and shows the progress. Apps with a window get a moment to close cleanly, the rest are ended.
- **Right-click** any process to stop just that one, stop all copies of it, or open its file location.
- **Windows system processes are never touched.**
- **No questions asked:** everything you started (browsers, editors, `node`, `bun`, terminals...) is stopped one by one in a single click. Windows processes and services are never touched.
- Optional **"Shut down PC when finished"**, with a 15-second cancel window.
- **Keep list:** apps you choose (Claude, Vibe Route and Stop Processing itself by default) are never stopped by Stop All. Right-click a process to add it, or manage the list in Settings.

### 2. Cleaner: delete junk files in one click
Cleans user and Windows temp files, the thumbnail cache, crash dumps, **browser cache** (Chrome, Edge, Brave, Opera, Vivaldi and Firefox, with logins and history kept), and optionally the Windows Update cache, Prefetch data and the Recycle Bin.
Files that are in use are skipped. You are asked once (Skip All / Stop), never per file. At the end it shows how much space was freed.

### 3. Power Fix: PC turns back on after shutdown
One click switches off everything in Windows that can power your PC back on: the network card's Wake-on-LAN (also from full shutdown), Fast Startup, wake timers, devices allowed to wake the PC (network card, mouse, keyboard), maintenance wake-up, auto sign-in after updates, and restart on system failure.
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
| **Startup Manager** | Turns programs that start with Windows on or off (nothing is deleted) |
| **Scheduled Shutdown** | Shuts the PC down after a delay or at a set time, optionally cleaning and stopping apps first |
| **Big Files Finder** | Shows the largest files and folders on a drive; delete sends them to the Recycle Bin |
| **Uninstall Leftovers** | Finds folders left behind by programs that are no longer installed |
| **App Uninstaller** | Lists every installed program and removes it together with the files and settings it leaves behind |

#### 🧹 App Uninstaller
Pick any installed program and press **Uninstall**. Its own uninstaller runs first; when it is done, BD All-in-One Tools looks for what the program left behind (its install folder, its data in AppData and ProgramData, and its settings in the registry) and removes it in one click.
- Items that clearly belong to the program are ticked for you; weaker matches are left for you to decide.
- Folders go to the **Recycle Bin**, and every registry key is **backed up** to a `.reg` file before it is removed.
- Folders used by another installed program are never touched.
- **Remove leftovers only** cleans up a program whose uninstaller is already gone.

#### 🗑️ Delete Permanently (built-in unlocker)
Paste the path of any **file or folder** (for example a huge `node_modules`) and it's erased **without going to the Recycle Bin**, together with everything inside it. A strong warning appears first, because this can't be undone.
It also deletes items that **won't delete normally**:
- very long paths, read-only, hidden and oddly-named files,
- **"Access denied"** items: it takes ownership and permission automatically,
- files **in use** by another program: it shows which program is holding them, and you choose **Close & Delete** or **Delete on Restart**.

Whole drives, Windows, Program Files and your main user folders are blocked for safety.

There is also a **This PC** panel with your Windows version, CPU, RAM, uptime and free space on every drive.

### 5. Runs quietly in the system tray
- The **✕** button hides the app to the tray. **Right-click** the tray icon and choose **Exit** to close it.
- **Starts with Windows** in the tray, with no UAC prompt.
- You can turn both behaviours off in **Settings → General**.

### 6. Automatic updates
- On by default. New versions download and install themselves, and every update is checked with a SHA-256 checksum before it's installed.
- Prefer to choose for yourself? Turn auto update off and use **Settings → Check for Updates**.

---

### 7. Settings
- **Clean temp files before shutting down** (off by default) runs the Cleaner before every Shut Down.
- **Alerts:** a tray message when one app uses too much RAM (1, 2 or 4 GB) or more than 80% CPU for 30 seconds.
- **Stop All keep list:** add or remove the apps that Stop All never closes.

## Requirements
- Windows 10 or 11, 64-bit
- Administrator rights. The app asks once when it starts, because changing power settings and cleaning system folders need them.

## Support
Found a problem or have an idea? Open an **[issue](../../issues)**.

## License
**Proprietary. © 2026 Vibe. All rights reserved.**
You may download and use the official release. You may not modify, reverse-engineer, redistribute or resell it. See [LICENSE](LICENSE).
