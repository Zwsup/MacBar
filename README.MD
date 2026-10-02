# MacBar

A lightweight, zero-dependency macOS-style auto-hiding top menu bar built natively for Windows using C# and Win32 APIs.

---

## Features

- **Zero External Dependencies:** Compiles directly using Windows' built-in C# compiler (`csc.exe`) without needing external SDKs or heavy runtimes.
- **Auto-Hide & Smooth Animation:** Slides down smoothly when the cursor touches the top edge of the screen and hides automatically when the cursor leaves.
- **No Fullscreen Invasions:** Does not interfere with standard Windows maximizing behavior or block window controls of other applications.
- **Dynamic Active Window Title:** Detects the foreground application in real-time and clears the title when the desktop or taskbar is active.
- **Hardware-Aware Power Detection:** Displays actual battery percentage and charging state on laptops, or automatically displays desktop mode on systems without a battery.
- **Minimal Resource Footprint:** Operates at ~10–15 MB RAM with near 0% idle CPU usage.

---

## Build Instructions

No external tools or IDEs required. Open PowerShell in the project directory containing `app.cs` and run:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:MacTopBar.exe app.cs