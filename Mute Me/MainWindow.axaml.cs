using System;
using Windows.Media.Capture;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Runtime.InteropServices;

namespace Mute_Me;

public partial class MainWindow : Window
{
    private IntPtr? _windowHandle;
    private const int HotKeyMessage = 0x0312; // Message ID for the hotkey message
    private const int HotKeyId = 9000; // ID for the hotkey to be registered.
    private const int ModifierAlt = 0x0001; // The ALT key
    private const int ModifierNone = 0x0000; // None
    private const int VirtualKeyF13 = 0x7C; // The letter N
    
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    
    public MainWindow()
    {
        InitializeComponent();
        
        Loaded += (_, _) => RegisterGlobalHotkey();
        Unloaded += (_, _) => UnregisterGlobalHotkey();
    }

    private void RegisterGlobalHotkey()
    {
        _windowHandle = TryGetPlatformHandle()?.Handle;
        if (_windowHandle == null) return;
        
        var result = RegisterHotKey(_windowHandle.Value, HotKeyId, ModifierNone, VirtualKeyF13);
        if (!result) return;

        Win32Properties.AddWndProcHookCallback(this, HotKeyCallback);
    }

    private IntPtr HotKeyCallback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {        
        // If not a hotkey message or the global hotkey for showing the window
        if (msg != HotKeyMessage || (int)wParam != HotKeyId) return IntPtr.Zero;

        MicrophoneController.SetMicMuted(!MicrophoneController.IsMuted);

        return IntPtr.Zero;
    }

    private void UnregisterGlobalHotkey()
    {
        if (_windowHandle == null) return;

        UnregisterHotKey(_windowHandle.Value, HotKeyId);
    }

    private void MuteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        MicrophoneController.SetMicMuted(true);
    }

    private void UnmuteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        MicrophoneController.SetMicMuted(false);

    }
}