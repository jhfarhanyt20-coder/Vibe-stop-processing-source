# Stop Processing — PC Shutdown Assistant

A small, dark-themed Windows app to run **right before you turn your PC off**.
Press one button and every non-system app is closed one by one. The app also cleans temporary files and fixes a PC that **turns itself back on after shutdown**.

> Windows 10 / 11 · single `.exe` · no installer · built on .NET Framework 4 (already part of Windows)

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
- It never follows junctions or symlinks out of a temp folder.

### 3. Power Fix: PC turns back on after shutdown
The app checks the Windows settings that can wake or restart a PC after you shut it down, and shows each one as **OK** or as a risk:

| Cause | What the fix does |
|---|---|
| Fast Startup (hybrid shutdown) | Turns it off, so shutdown becomes a real full shutdown |
| Wake timers | Disables them (AC and battery) |
| Devices allowed to wake the PC (network card / Wake-on-LAN, mouse, keyboard) | Disables wake for each device |
| Automatic maintenance wake-up | Turns it off |
| Auto sign-in / restart after updates | Turns it off |
| Restart on system failure | Turns it off, so the PC stays off if shutdown crashes |

- **Fix Auto Power-On** applies all of the above in one click.
- **Restore Defaults** undoes all of it, including re-enabling the exact devices that were disabled.
- **Full Shut Down Now** does a proper full shutdown (`shutdown /s`).

> **Still turning on by itself?** Some causes live in the BIOS/UEFI. Restart into BIOS (usually `Del` or `F2`) and set
> *Restore on AC Power Loss → Power Off*, *Wake on LAN / PCIe (PME) → Disabled*, *RTC Alarm → Disabled*, *ErP → Enabled*.

> After the fix, the mouse and keyboard can no longer wake the PC from **Sleep**. Use the power button instead.

### Interface
- Custom title bar: no Windows frame. It has its own minimize, maximize and close buttons, you drag it to move the window, and double-clicking it maximizes the window.
- Dark, card-based layout with a sidebar menu.
- Themed dialogs instead of the default Windows message boxes.

---

## How to use

1. Download `StopProcessing.exe` (or build it yourself, see below).
2. Double-click it and press **Yes** on the UAC prompt. The app needs administrator rights to change power settings and to clean `C:\Windows\Temp`.
3. Before turning the PC off:
   - **Cleaner** → **Clean Now**
   - **Processes** → tick *Shut down PC when finished* → **Stop All Processes**
4. If your PC wakes up on its own: **Power Fix** → **Fix Auto Power-On**, which you only need to do once.

---

## Build from source

No Visual Studio needed. The C# compiler ships with Windows. Open PowerShell in the project folder and run:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:StopProcessing.exe /win32manifest:app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll StopProcessing.cs
```

| File | Purpose |
|---|---|
| `StopProcessing.cs` | All of the app's source code (WinForms, C# 5) |
| `app.manifest` | Asks Windows to run the app as administrator |
| `StopProcessing.exe` | Ready-to-run build |

---

## Safety notes
- System processes and anything inside the Windows folder are protected from **Stop All**.
- The Cleaner only deletes inside the listed temp/cache folders. Folders that are still in use are left alone.
- Every Power Fix change can be undone with **Restore Defaults**. The list of disabled wake devices is saved in `%LOCALAPPDATA%\StopProcessing\wake_devices.txt`.
- Deleted temp files and an emptied Recycle Bin **cannot be recovered**.

## License
MIT. Free to use and modify.
