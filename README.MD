# MacBar

A lightweight, zero-dependency macOS-style auto-hiding top menu bar built natively for Windows using C# and Win32 APIs.

---

## Features

- **Zero External Dependencies:** Compiles directly using Windows' built-in C# compiler (`csc.exe`) without needing external SDKs or heavy runtimes.
- **Auto-Hide & Smooth Animation:** Employs an exponential Ease-Out (LERP) deceleration curve that slides down smoothly when the cursor touches the top edge of the screen and hides automatically when the cursor leaves.
- **Spotlight-Style Quick Search:** Built-in collapsible search bar enabling direct Google queries in the default web browser via keyboard shortcuts (`Enter` to submit, `Esc` to dismiss).
- **Modern Pill Design:** Built with a dark palette featuring semi-transparent rounded pill hover states and a subtle bottom divider line.
- **Now Playing & Media Controls:** Detects active media playback titles in real-time and provides a quick context menu for playback control.
- **Integrated Volume Slider:** Hover-activated volume popup supporting direct mouse wheel adjustment for system master audio.
- **Dynamic Active Window Title:** Detects the foreground application in real-time and clears the title when the desktop or taskbar is focused.
- **Hardware-Aware Power Detection:** Displays actual battery percentage and charging state on laptops, or automatically displays desktop mode on systems without a battery.
- **Live Hardware Monitoring:** Displays real-time CPU and RAM utilization metrics directly on the bar with zero system latency.
- **Running App Switcher:** Dynamically extracts and renders high-DPI icons for visible foreground windows, allowing single-click window focusing.
- **Leak-Free Resource Management:** Explicit GDI bitmap disposal, Icon handle deallocation via `DestroyIcon`, and graceful system counter cleanup to ensure persistent low footprint (~10-15 MB RAM).

---

## Build Instructions

No external tools or IDEs required. Open PowerShell in the project directory containing `app.cs` and run:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:MacTopBar.exe app.cs