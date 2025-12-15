# 🎙️ MuteMe

**MuteMe** is a lightweight, high-performance Windows utility that allows you to mute/unmute your global system microphone via a customizable keyboard shortcut (Hotkey).

Designed specifically for **gamers**, it utilizes **Raw Input** to detect keystrokes with zero latency, working seamlessly even over **Fullscreen Exclusive** games or applications with elevated privileges, without losing focus.

![Status](https://img.shields.io/badge/Platform-Windows-blue)
![Build](https://img.shields.io/badge/.NET-9.0-purple)

## ✨ Key Features

*   **Reliable Global Hotkey:** Uses Win32 `Raw Input` APIs (instead of standard hooks) to intercept keys. This ensures compatibility with competitive games (Valorant, COD, CS2, etc.) and anti-cheat systems.
*   **Audio Feedback:** Plays confirmation sounds (Mute On/Off). It features a custom PCM byte manipulation engine to handle volume control without relying on heavy external libraries.
*   **Visual Feedback:** Displays minimal, non-intrusive popups for volume adjustment and hotkey recording.
*   **System Tray Only:** No cluttered windows. The app lives quietly in the System Tray.
*   **Ultra Lightweight:** Compiled using **Native AOT** (Ahead-of-Time). The result is a single, self-contained `.exe` file that runs instantly and does not require the .NET Runtime to be installed.

## 🚀 Installation

1.  Go to the [**Releases**](../../releases) section of this repository.
2.  Download the latest `MuteMe.exe`.
3.  Place it anywhere you like (it is portable).
4.  Run the application (Recommended: **Run as Administrator**).

## 🎮 Usage

The application runs in the background and is accessible via the **System Tray** (near the Windows clock).

### Context Menu (Right Click)
Right-click the tray icon to access:

*   **Change Hotkey:** Opens a popup to record a new key combination.
    *   Supports single keys (e.g., `F13`, `M`, `P`).
    *   Supports modifier combinations (e.g., `CTRL + M`, `SHIFT + F10`).
*   **Adjust Sound Volume:** Opens a slider to control the volume of the feedback beeps.
*   **Exit:** Closes the application.

## ⚙️ Requirements

*   **OS:** Windows 10 or Windows 11 (x64).
*   **Privileges:** To function correctly over games running as Administrator (often required by anti-cheats), MuteMe must also be run as Administrator.

## 🛠️ Build from Source

If you want to modify the code or build the app yourself:

**Prerequisites:**
*   [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

**Commands:**

## Clone the repository
```git clone https://github.com/LucaFagnoni/mute-me.git```

## Publish in Release mode (Single File, Self Contained, Native AOT)
dotnet publish -c Release -r win-x64


*The executable will be located in `bin/Release/net9.0-windows/win-x64/publish/`.*

## 🧩 Tech Stack

*   **Framework:** .NET 9.0
*   **UI:** [Avalonia UI](https://avaloniaui.net/) (for TrayIcon and minimal Popups).
*   **Input:** Win32 `RegisterRawInputDevices` + `GetAsyncKeyState`.
*   **Audio:** Win32 `winmm.dll` (`PlaySound`) with real-time PCM byte scaling for volume control.
*   **Deployment:** Native AOT / Single File trimming.
