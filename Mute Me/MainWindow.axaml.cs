using System;
using Windows.Media.Capture;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Mute_Me;

public partial class MainWindow : Window
{
    private IntPtr? _windowHandle;
    
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    
    private const int HotKeyMessage = 0x0312; // Message ID for the hotkey message
    private const int HotKeyId = 9000; // ID for the hotkey to be registered.
    private const int ModifierAlt = 0x0001; // The ALT key
    private const int ModifierNone = 0x0000; // None
    private const int VirtualKeyF13 = 0x7C; // The letter N
    
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    
    private static Avalonia.Media.Imaging.Bitmap mutedImg;
    private static Avalonia.Media.Imaging.Bitmap unmutedImg;
    
    public MainWindow()
    {
        InitializeComponent();
        
        using var stream = AssetLoader.Open(new Uri("avares://MuteMe/Assets/muted.png"));
        mutedImg = new Avalonia.Media.Imaging.Bitmap(stream);
        stream.Dispose();
        
        using var stream2 = AssetLoader.Open(new Uri("avares://MuteMe/Assets/unmuted.png"));
        unmutedImg = new Avalonia.Media.Imaging.Bitmap(stream2);
        stream2.Dispose();
        
        Opened += (_, _) => EnableClickThrough();
        Loaded += (_, _) => RegisterGlobalHotkey();
        Unloaded += (_, _) => UnregisterGlobalHotkey();
    }

    private void EnableClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
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
        microphoneStatusIcon.Source = MicrophoneController.IsMuted ? mutedImg : unmutedImg;

        return IntPtr.Zero;
    }

    private void UnregisterGlobalHotkey()
    {
        if (_windowHandle == null) return;

        UnregisterHotKey(_windowHandle.Value, HotKeyId);
    }
}