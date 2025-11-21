using System;
using Avalonia.Controls;
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
    private const int ModifierNone = 0x0000; // None
    private const int ModifierAlt = 0x0001; // The ALT key
    // private const int VirtualKeyF13 = 0x7C;
    private const int VirtualKeyF13 = 124;

    private SoundManager SoundManager;
    
    #region Gemini
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT   = 0x10;
    private const int VK_MENU    = 0x12; // ALT

// Codici Virtual Key per i tasti funzione
    private const int VK_F9  = 0x78; // 120
    private const int VK_F10 = 0x79; // 121
    #endregion
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    
    private static Avalonia.Media.Imaging.Bitmap mutedImg;
    private static Avalonia.Media.Imaging.Bitmap unmutedImg;
    
    private HiddenRawInputWindow hiddenWindowInstance = null;
    
    
    public MainWindow()
    {
        InitializeComponent();
        
        #region Load Images
        using var stream = AssetLoader.Open(new Uri("avares://MuteMe/Assets/muted.png"));
        mutedImg = new Avalonia.Media.Imaging.Bitmap(stream);
        stream.Dispose();
        
        using var stream2 = AssetLoader.Open(new Uri("avares://MuteMe/Assets/unmuted.png"));
        unmutedImg = new Avalonia.Media.Imaging.Bitmap(stream2);
        stream2.Dispose();
        #endregion
        
        #region Load Sounds
        Uri uriMuteSnd = new Uri("avares://MuteMe/Assets/mute.wav");
        Uri uriUnmuteSnd = new Uri("avares://MuteMe/Assets/unmute.wav");
        
        SoundManager = new SoundManager(uriMuteSnd, uriUnmuteSnd);
        #endregion
        
        try
        {
            hiddenWindowInstance = new HiddenRawInputWindow();
            hiddenWindowInstance.KeyPressed += OnRawKeyPressed;
            hiddenWindowInstance.Start();
        }
        catch (Exception ex)
        { }
        
        Opened += (_, _) => EnableClickThrough();
    }

    private void EnableClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    private void OnRawKeyPressed(int vKey)
    {
        if (vKey != VirtualKeyF13) return; // la tua hotkey

        MicrophoneController.SetMicMuted(!MicrophoneController.IsMuted);
        microphoneStatusIcon.Source = MicrophoneController.IsMuted ? mutedImg : unmutedImg;
        if (MicrophoneController.IsMuted)
        {
            SoundManager.PlayMuted();
        }
        else
        {
            SoundManager.PlayUnmuted();
        }
    }
    
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Pulizia importante alla chiusura
        hiddenWindowInstance?.Stop();
        base.OnClosing(e);
    }
}