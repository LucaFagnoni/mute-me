# 🎙️ MuteMe

**MuteMe** is a lightweight, high-performance Windows utility that allows you to mute/unmute your global system microphone via a customizable keyboard shortcut (Hotkey).

Designed specifically for **gamers**, it utilizes **Raw Input** to detect keystrokes with zero latency, working seamlessly even over **Fullscreen Exclusive** games or applications with elevated privileges, without losing focus.

![Status](https://img.shields.io/badge/Platform-Windows-blue)
![Build](https://img.shields.io/badge/C-Win32-blue)
![Size](https://img.shields.io/badge/Size-420%20KB-brightgreen)

## ✨ Key Features

*   **Reliable Global Hotkey:** Uses Win32 `Raw Input` APIs (instead of standard hooks) to intercept keys. This ensures compatibility with competitive games (Valorant, COD, CS2, etc.) and anti-cheat systems.
*   **Responsive Under Load:** The hotkey runs on its own time-critical thread that never waits on the UI or Explorer, and the process keeps its pages resident, so muting still works while a game compiles shaders or the system is paging.
*   **Audio Feedback:** Plays confirmation sounds (Mute On/Off). Sounds play in the shared Windows system-sounds session, so MuteMe never shows up as an app in any volume mixer (Windows or EarTrumpet); the **System Sounds** slider also scales them.
*   **Visual Feedback:** A small click-through overlay in a screen corner shows the current mic state at a glance. It scales with the display DPI and is excluded from screenshots, recordings and screen sharing. Settings live in an iOS-style panel opened from the tray, which exists only while it is open.
*   **System Tray Only:** No cluttered windows. The app lives quietly in the System Tray.
*   **Ultra Lightweight:** Written in plain C against the Win32 API. A single ~420 KB `.exe` with no runtime, no framework and no DLLs to ship alongside: it links only against libraries Windows already has. Around 3.5 MB of private memory at rest.

## 🚀 Installation

1.  Go to the [**Releases**](../../releases) section of this repository.
2.  Download the latest `MuteMe.exe`.
3.  Place it anywhere you like (it is portable).
4.  Run the application (Recommended: **Run as Administrator**).

## 🎮 Usage

The application runs in the background and is accessible via the **System Tray** (near the Windows clock).

### Settings Panel (Right Click)
Right-click the tray icon to open the panel (double-click toggles mute). It follows the Windows light/dark setting and closes when you click elsewhere or press `Esc`.

*   **Microphone:** Switch to mute/unmute, with the current state and hotkey.
*   **Key:** Click, then press the key to bind (`Esc` cancels). The current binding is shown by name (`F13`, `A`, `Pause`), resolved through your active keyboard layout.
    *   Keys are stored by hardware scancode, so a binding survives switching keyboard layout.
    *   **Modifier** (`None`, `Shift`, `Ctrl`, `Alt`): the chosen modifier must be held; any other modifier held at the same time is ignored, so the hotkey still fires while you sprint or crouch.
*   **Volume:** Feedback sound volume, as a slider; a preview plays when you release it.
*   **Corner / Margin:** Which screen corner holds the overlay, plus its margin in pixels.
*   **Start at login:** Registers a scheduled task so the app starts elevated at logon.
*   **Quit Mute Me:** Closes the application.

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
src\build.bat
```

`build.bat` locates MSVC and the Windows SDK through `vswhere`, so it needs no developer prompt. Add `debug` (`src\build.bat debug`) for an unoptimised build with symbols.

*The executable will be located in `src/build/MuteMe.exe`.*

## 🧩 Tech Stack

*   **Language:** C (C11), MSVC, no third-party dependencies.
*   **UI:** Win32 `Shell_NotifyIcon` for the tray; the settings panel is drawn by hand into a DIB (anti-aliased rounded shapes from a distance function, GDI ClearType text, DWM rounded corners); a `WS_EX_LAYERED` click-through window drawn with `UpdateLayeredWindow` for the overlay.
*   **Input:** Win32 `RegisterRawInputDevices` (scancodes, layout independent) + `GetAsyncKeyState` for modifiers, on a dedicated `THREAD_PRIORITY_TIME_CRITICAL` thread.
*   **Audio:** WASAPI `IAudioEndpointVolume::SetMute` for the microphone; `PlaySound` with `SND_SYSTEM` for feedback, with PCM byte scaling for volume.
*   **Assets:** icons pre-rendered to premultiplied BGRA and embedded as `RCDATA`, so no image decoder is linked in.
*   **Always on top:** `SetWinEventHook` on `EVENT_SYSTEM_FOREGROUND`, no polling timer.
*   **Self-healing:** a 5 s watchdog timer re-registers raw input and re-adds the tray icon if Explorer dropped it.
