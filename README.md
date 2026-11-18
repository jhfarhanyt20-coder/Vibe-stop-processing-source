# Vibe Stop Processing: PC Shutdown Assistant

A dark-themed Windows app by **Vibe** to run **right before you turn your PC off**.
Press one button and every non-system app is closed one by one. The app also cleans temporary files, fixes a PC that **turns itself back on after shutdown**, and **keeps itself up to date**.

> Windows 10 / 11 · single `.exe` · no installer · runs on .NET Framework 4, which is already part of Windows

## Download
Get the latest `StopProcessing.exe` from the **[Releases page](../../releases/latest)**.

---

## Features

### 1. Processes: stop everything before shutdown
- Shows running processes live, with CPU, memory and a status for each one.
- **Stop All Processes** closes apps one at a time and shows the progress.
- **Windows system processes are never touched.** That covers `explorer`, `svchost`, `lsass`, Defender, drivers and anything in `C:\Windows`.
- Important apps such as browsers, Office, Discord and Steam **ask before closing**, so you don't lose unsaved work.
- If an app doesn't close on its own, the app asks whether to force-stop it.
- Optional **"Shut down PC when finished"**: the PC powers off 15 seconds after the run, and you can cancel it in that time.

### 2. Cleaner: delete temp files in one click
| Location | Default |
|---|---|
| User temp files (`%TEMP%`) | ✅ |
| Windows temp files (`C:\Windows\Temp`) | ✅ |
| Thumbnail cache | ✅ |
| Crash dumps and error reports | ✅ |
| Windows Update download cache | ⬜ |
| Prefetch data | ⬜ |
| Recycle Bin | ⬜ |

- **Clean Now** deletes everything in the ticked locations.
- **Files that are in use are skipped.** A dialog asks **Skip / Skip All / Stop Cleaning**. You can also untick *"Ask me before skipping locked files"* to skip them silently.
- At the end it shows how much space was freed, how many files were deleted and how many were skipped.

### 3. Power Fix: PC turns back on after shutdown
| Cause | What the fix does |
|---|---|
| Fast Startup (hybrid shutdown) | Turns it off, so shutdown becomes a real full shutdown |
| Wake timers | Disables them |
| Devices allowed to wake the PC (network card / Wake-on-LAN, mouse, keyboard) | Disables wake for each device |
| Automatic maintenance wake-up | Turns it off |
| Auto sign-in / restart after updates | Turns it off |
| Restart on system failure | Turns it off, so the PC stays off if shutdown crashes |

- **Fix Auto Power-On** applies all of the above in one click. **Restore Defaults** undoes it.
- **Full Shut Down Now** does a proper full shutdown.

> **Still turning on by itself?** Open the BIOS (usually `Del` or `F2`) and set *Restore on AC Power Loss → Power Off*, *Wake on LAN → Disabled*, *RTC Alarm → Disabled*, *ErP → Enabled*.

### 4. Settings: automatic updates
- **Install updates automatically** is on by default. A few seconds after the app starts, it checks for a new version, downloads it and installs it, then asks whether to restart now.
- With auto update **off**, the app tells you a new version exists and you install it yourself with **Check for Updates → Install Update**.
- Each update is checked before it's installed: the file size, the SHA-256 checksum and whether it's a valid program. If anything fails, the old version is kept.
- Release notes for each version are shown in the app.

---

## How to use
1. Download `StopProcessing.exe` from the [Releases page](../../releases/latest).
2. Double-click it and press **Yes** on the UAC prompt. The app needs administrator rights to change power settings and to clean `C:\Windows\Temp`.
3. Before turning the PC off:
   - **Cleaner** → **Clean Now**
   - **Processes** → tick *Shut down PC when finished* → **Stop All Processes**
4. If your PC wakes up on its own: **Power Fix** → **Fix Auto Power-On**. You only need to do this once.

---

## For the publisher: releasing an update
Bump the version, build, push and create the GitHub release with one command:

```powershell
.\release.ps1 -Version 2.2.0 -Notes "Added X, fixed Y"
```

Every installed copy finds the new release on its next start. Copies with auto update on install it themselves, and the rest show an "Update available" prompt.
Requirements: Git and GitHub CLI (`gh`) signed in to the repository owner's account.

---

## Safety notes
- System processes and anything inside the Windows folder are protected from **Stop All**.
- The Cleaner only deletes inside the listed temp/cache folders and never follows junctions or symlinks.
- Every Power Fix change can be undone with **Restore Defaults**.
- Deleted temp files and an emptied Recycle Bin **cannot be recovered**.

## License
**Proprietary. © 2026 Vibe. All rights reserved.**
You may download and use the official release. You may not modify, reverse-engineer, redistribute or resell it. See [LICENSE](LICENSE).
