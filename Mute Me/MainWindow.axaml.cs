using System;
using Avalonia.Controls;
using System.Runtime.InteropServices;
using Avalonia;
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
    private static WindowIcon mutedIcon;
    private static WindowIcon unmutedIcon;

    
    private HiddenRawInputWindow hiddenWindowInstance = null;
    
    
    // Variabile per salvare il volume (0-100)
    private int _soundVolume = 100; 
    
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    private NativeMenuItem _muteBtn;
    private TrayIcon _trayIcon;
    
    public MainWindow()
    {
        InitializeComponent();
        
        #region Load Images
        using var stream = AssetLoader.Open(new Uri("avares://MuteMe/Assets/muted.png"));
        mutedImg = new Avalonia.Media.Imaging.Bitmap(stream);
        
        using var stream2 = AssetLoader.Open(new Uri("avares://MuteMe/Assets/unmuted.png"));
        unmutedImg = new Avalonia.Media.Imaging.Bitmap(stream2);
        
        mutedIcon = new WindowIcon(stream);
        unmutedIcon = new WindowIcon(stream2);
        stream.Dispose();
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
        
        SetupTrayIcon();
        
        Opened += (_, _) => EnableClickThrough();
    }

    private void EnableClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }
    
    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            Icon = unmutedIcon,
            ToolTipText = "Mute Me"
        };

        var menu = new NativeMenu();

        // 1. SFX Volume
        var volumeItem = new NativeMenuItem("Sfx Volume");
        volumeItem.Click += (s, e) => OpenVolumePopup();
        menu.Add(volumeItem);
        
        // 2. Mute Button
        _muteBtn = new NativeMenuItem("Mute");
        _muteBtn.ToggleType = NativeMenuItemToggleType.CheckBox;
        _muteBtn.IsChecked = false;
        _muteBtn.Click += (s, e) => ToggleMicrophone();
        menu.Add(_muteBtn);

        // 3. Separator
        menu.Add(new NativeMenuItemSeparator());
        
        // 4. Uscita
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (s, e) => Environment.Exit(0);
        menu.Add(exitItem);

        _trayIcon.Menu = menu;
        _trayIcon.IsVisible = true;
    }

    private void OpenVolumePopup()
    {
        // Ottieni la posizione del mouse
        var mousePos = this.PointToScreen(new Point(0,0)); // Fallback
    
        // Cerchiamo di prendere la posizione del mouse globale
        // Nota: In Avalonia Desktop la posizione precisa del mouse globale richiede un piccolo hack o TopLevel
        // Per semplicità, centriamo sul mouse o usiamo una posizione fissa se non disponibile.
    
        var popup = new VolumePopup(_soundVolume);
    
        // Iscriviti al cambio volume
        popup.VolumeChanged += (newVol) => 
        {
            _soundVolume = newVol;
            // Qui aggiornerai il SoundManager (vedi sotto)
            if (SoundManager != null) SoundManager.Volume = _soundVolume;
        };

        // Posizionamento vicino al cursore
        // (Questo posiziona la finestra dove sta il puntatore del mouse in pixel schermo)
        if (Screens.Primary != null)
        {
            // Necessita Avalonia.Desktop
            // Metodo rapido per ottenere il mouse (funziona su Windows)
            [DllImport("user32.dll")]
            static extern bool GetCursorPos(out POINT lpPoint);
            GetCursorPos(out POINT p);
        
            popup.Position = new PixelPoint(p.X - 100, p.Y - 60); // Centra rispetto al cursore
        }

        popup.Show();
        popup.Activate(); // Dà il focus così l'evento Deactivated funziona
    }
    
    private void OnRawKeyPressed(int vKey)
    {
        if (vKey != VirtualKeyF13) return; // la tua hotkey

        ToggleMicrophone();
    }

    private void ToggleMicrophone()
    {
        MicrophoneController.SetMicMuted(!MicrophoneController.IsMuted);
        microphoneStatusIcon.Source = MicrophoneController.IsMuted ? mutedImg : unmutedImg;
        if (MicrophoneController.IsMuted)
        {
            SoundManager.PlayMuted();
            _muteBtn.IsChecked = true;
            _trayIcon.Icon = mutedIcon;
        }
        else
        {
            SoundManager.PlayUnmuted();
            _muteBtn.IsChecked = false;
            _trayIcon.Icon = unmutedIcon;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Pulizia importante alla chiusura
        hiddenWindowInstance?.Stop();
        base.OnClosing(e);
    }
}