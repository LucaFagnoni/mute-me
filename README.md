# 🎙️ MuteMe

**MuteMe** is a lightweight, high-performance Windows utility that allows you to mute/unmute your global system microphone via a customizable keyboard shortcut (Hotkey).

Designed specifically for **gamers**, it utilizes **Raw Input** to detect keystrokes with zero latency, working seamlessly even over **Fullscreen Exclusive** games or applications with elevated privileges, without losing focus.

![Status](https://img.shields.io/badge/Platform-Windows-blue)
![Build](https://img.shields.io/badge/C-Win32-blue)
![Size](https://img.shields.io/badge/Size-395%20KB-brightgreen)

## ✨ Key Features

*   **Reliable Global Hotkey:** Uses Win32 `Raw Input` APIs (instead of standard hooks) to intercept keys. This ensures compatibility with competitive games (Valorant, COD, CS2, etc.) and anti-cheat systems.
*   **Audio Feedback:** Plays confirmation sounds (Mute On/Off). It features a custom PCM byte manipulation engine to handle volume control without relying on heavy external libraries.
*   **Visual Feedback:** A small click-through overlay in a screen corner shows the current mic state at a glance. Settings live entirely in the native tray menu, so no extra windows are ever created.
*   **System Tray Only:** No cluttered windows. The app lives quietly in the System Tray.
*   **Ultra Lightweight:** Written in plain C against the Win32 API. A single ~395 KB `.exe` with no runtime, no framework and no DLLs to ship alongside: it links only against libraries Windows already has. Around 2.7 MB of private memory and 4 threads at rest.

## 🚀 Installation

1.  Go to the [**Releases**](../../releases) section of this repository.
2.  Download the latest `MuteMe.exe`.
3.  Place it anywhere you like (it is portable).
4.  Run the application (Recommended: **Run as Administrator**).

## 🎮 Usage

The application runs in the background and is accessible via the **System Tray** (near the Windows clock).

### Context Menu (Right Click)
Right-click the tray icon to access:

*   **Set Hotkey:** Records the next key you press. The menu shows the current binding by name (`F13`, `A`, `Pause`), resolved through your active keyboard layout.
    *   Keys are stored by hardware scancode, so a binding survives switching keyboard layout.
    *   Supports modifier combinations via the **Modifiers** submenu (`Shift`, `Ctrl`, `Alt`).
*   **Sfx Volume:** Feedback beep volume, in steps (0 / 20 / 40 / 60 / 80 / 100 %).
*   **Icon Position:** Which screen corner holds the overlay, plus its margin in pixels.
*   **Auto Start (Admin):** Registers a scheduled task so the app starts elevated at logon.
*   **Exit:** Closes the application.

## ⚙️ Requirements

*   **OS:** Windows 10 or Windows 11 (x64).
*   **Privileges:** To function correctly over games running as Administrator (often required by anti-cheats), MuteMe must also be run as Administrator.

## 🛠️ Build from Source

If you want to modify the code or build the app yourself:

**Prerequisites:**
*   Visual Studio Build Tools with the **Desktop development with C++** workload (MSVC + Windows SDK). Nothing else.

**Commands:**

```
git clone https://github.com/LucaFagnoni/mute-me.git
cd mute-me
srcuild.bat
```

`build.bat` locates MSVC and the Windows SDK through `vswhere`, so it needs no developer prompt. Add `debug` (`srcuild.bat debug`) for an unoptimised build with symbols.

*The executable will be located in `src/build/MuteMe.exe`.*

## 🧩 Tech Stack

*   **Language:** C (C11), MSVC, no third-party dependencies.
*   **UI:** Win32 `Shell_NotifyIcon` + `TrackPopupMenu` for the tray, and a `WS_EX_LAYERED` click-through window drawn with `UpdateLayeredWindow` for the overlay.
*   **Input:** Win32 `RegisterRawInputDevices` (scancodes, layout independent) + `GetAsyncKeyState` for modifiers.
*   **Audio:** WASAPI `IAudioEndpointVolume::SetMute` for the microphone; `winmm.dll` `PlaySound` for feedback, with PCM byte scaling for volume.
*   **Assets:** icons pre-rendered to premultiplied BGRA and embedded as `RCDATA`, so no image decoder is linked in.
*   **Always on top:** `SetWinEventHook` on `EVENT_SYSTEM_FOREGROUND`, no polling timer.
