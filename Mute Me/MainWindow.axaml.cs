using Avalonia.Controls;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Mute_Me;

public partial class MainWindow : Window
{
    // WINDOW STYLES
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    
    // MODIFIER KEYS
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // ALT

    // MODIFIER REFERENCES
    private NativeMenuItem _modifierNone; 
    private NativeMenuItem _modifierShift; 
    private NativeMenuItem _modifierCtrl; 
    private NativeMenuItem _modifierAlt; 
    
    // MANAGERS
    private SettingsManager _settingsManager;
    private SoundManager _soundManager;
    private AutoStartManager _autoStartManager;
    private HiddenRawInputWindow _hiddenWindowInstance;
    
    // UI RESOURCES
    private static WindowIcon? _mutedIcon;
    private static WindowIcon? _unmutedIcon;
    private static Avalonia.Media.Imaging.Bitmap? _mutedImg;
    private static Avalonia.Media.Imaging.Bitmap? _unmutedImg;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _hotkeyMenuItem;
    private NativeMenuItem? _muteBtn;
    
    // STATE
    private bool _isRecording = false;
    private HotkeyPopup? _activePopup; 
    
    public MainWindow()
    {
        InitializeComponent();
        
        _settingsManager = SettingsManager.Load();
        _autoStartManager = new AutoStartManager();
        
        // string assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "Mute_Me";
        
        #region Load Images
        try 
        {
            var uriMuted = $"avares://MuteMe/Assets/muted.png";

            _mutedImg = LoadBitmap(uriMuted);
            _mutedIcon = LoadIcon(uriMuted);
            
            var uriUnmuted =$"avares://MuteMe/Assets/unmuted.png";
            _unmutedImg = LoadBitmap(uriUnmuted);
            _unmutedIcon = LoadIcon(uriUnmuted);
        }
        catch (Exception ex)
        {
            _mutedIcon = this.Icon;
            _unmutedIcon = this.Icon;
        }
        #endregion
        
        #region Load Sounds
        try
        {
            Uri uriMuteSnd = new Uri("avares://MuteMe/Assets/mute.wav");
            Uri uriUnmuteSnd = new Uri("avares://MuteMe/Assets/unmute.wav");
            Uri uriNotiSnd = new Uri("avares://MuteMe/Assets/notification.wav");
            
            _soundManager = new SoundManager(uriMuteSnd, uriUnmuteSnd, uriNotiSnd);
            _soundManager.Volume = _settingsManager.SfxVolume;
        }
        catch (Exception ex) { }
        #endregion
        
        #region Setup Raw Input
        try
        {
            _hiddenWindowInstance = new HiddenRawInputWindow();
            _hiddenWindowInstance.KeyPressed += OnRawKeyPressed;
            _hiddenWindowInstance.Start();
        }
        catch (Exception ex) { }
        #endregion
        
        SetupTrayIcon();
        
        Opened += (_, _) => EnableClickThrough();
    }

    private WindowIcon? LoadIcon(string uri)
    {
        try 
        { 
            using var s = AssetLoader.Open(new Uri(uri)); 
            return new WindowIcon(s); 
        } 
        catch { return this.Icon; }
    }
    
    private Avalonia.Media.Imaging.Bitmap? LoadBitmap(string uri)
    {
        try 
        { 
            using var s = AssetLoader.Open(new Uri(uri)); 
            return new Avalonia.Media.Imaging.Bitmap(s); 
        } 
        catch { return null; }
    }
    
    private void EnableClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }
    
    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            Icon = _unmutedIcon,
            ToolTipText = "Mute Me"
        };

        var menu = new NativeMenu();

        #region Tray Icon Menu Setup
        // 1. SFX Volume
        var volumeItem = new NativeMenuItem("Sfx Volume");
        volumeItem.Click += (_, _) => OpenVolumePopup();
        menu.Add(volumeItem);
        
        // 2. Mute Button
        _muteBtn = new NativeMenuItem("Mute");
        _muteBtn.ToggleType = NativeMenuItemToggleType.CheckBox;
        _muteBtn.IsChecked = false;
        _muteBtn.Click += (s, e) => ToggleMicrophone();
        menu.Add(_muteBtn);
        
        // 3. Modifiers Submenu
        var modifierMenuItem = new NativeMenuItem("Modifiers");
        var modMenu = new NativeMenu();
        modMenu.Add(CreateRadioItem("None", () => SetModifier(false, false, false), _settingsManager is {RequireShift: false, RequireCtrl: false, RequireAlt: false}));
        modMenu.Add(CreateRadioItem("Shift", () => SetModifier(true, false, false), _settingsManager.RequireShift));
        modMenu.Add(CreateRadioItem("Ctrl", () => SetModifier(false, true, false), _settingsManager.RequireCtrl));
        modMenu.Add(CreateRadioItem("Alt", () => SetModifier(false, false, true), _settingsManager.RequireAlt));
        modifierMenuItem.Menu = modMenu;
        menu.Add(modifierMenuItem);
        
        // 4. Set Hotkey Button
        _hotkeyMenuItem = new NativeMenuItem($"Set Hotkey (Current: {_settingsManager.CurrentHotkey})");
        _hotkeyMenuItem.Click += (_, _) => StartRecordingHotkey();
        menu.Add(_hotkeyMenuItem);
        
        // 5. Set Admin Auto Start with Windows
        var autoStartItem = new NativeMenuItem("Loading...");
        autoStartItem.ToggleType = NativeMenuItemToggleType.CheckBox;
        InitializeAutoStartItem(autoStartItem);
        menu.Add(autoStartItem);

        // 6. Separator
        menu.Add(new NativeMenuItemSeparator());
        
        // 7. Uscita
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _hiddenWindowInstance?.Stop();
            _soundManager?.Dispose();
            Environment.Exit(0);
        };
        menu.Add(exitItem);

        _trayIcon.Menu = menu;
        _trayIcon.IsVisible = true;
        #endregion
    }
    
    private NativeMenuItem CreateRadioItem(string text, Action action, bool isChecked)
    {
        var item = new NativeMenuItem(text) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetModifier(bool shift, bool crtl, bool alt)
    {
        _settingsManager.RequireShift = shift;
        _settingsManager.RequireCtrl = crtl;
        _settingsManager.RequireAlt = alt;
        _settingsManager.Save();
    }
    
    private async void InitializeAutoStartItem(NativeMenuItem item)
    {
        bool active = await _autoStartManager.IsStartupTaskActiveAsync();
        UpdateAutoStartItem(item, active);

        item.Click += async (_, _) =>
        {
            item.IsEnabled = false;
            item.Header = "Working...";
            
            if (await _autoStartManager.IsStartupTaskActiveAsync())
                await _autoStartManager.UninstallStartupTaskAsync();
            else
                await _autoStartManager.InstallStartupTaskAsync();

            bool newState = await _autoStartManager.IsStartupTaskActiveAsync();
            UpdateAutoStartItem(item, newState);
            item.IsEnabled = true;
        };
    }
    
    private void UpdateAutoStartItem(NativeMenuItem item, bool active)
    {
        Dispatcher.UIThread.Post(() =>
        {
            item.IsChecked = active;
            item.Header = "Auto Start (Admin)";
        });
    }
    
    private void StartRecordingHotkey()
    {
        if (_isRecording) return;

        _isRecording = true;
        _soundManager.PlayNoti();
        
        Dispatcher.UIThread.Post(() =>
        {
            _activePopup = new HotkeyPopup();
            _activePopup.Canceled += () => _isRecording = false;
            _activePopup.Show();
        });
    }

    private void OpenVolumePopup()
    {
        var popup = new VolumePopup(_settingsManager.SfxVolume);
        
        popup.VolumeChanged += (newVol) => 
        {
            _settingsManager.SfxVolume = newVol;
            _soundManager.Volume = newVol;
            _settingsManager.Save();
        };

        // Place near the cursor
        if (Screens.Primary != null)
        {
            [DllImport("user32.dll")]
            static extern bool GetCursorPos(out POINT lpPoint);
            GetCursorPos(out POINT p);
            popup.Position = new PixelPoint(p.X - 100, p.Y - 60);
        }

        popup.Show();
        popup.Activate();
    }
    
    private void OnRawKeyPressed(int vKey)
    {
        if (_isRecording)
        {
            if (IsModifierKey(vKey)) return; // Ignores CTRL/ALT alone
            
            _settingsManager.CurrentHotkey = vKey;
            _isRecording = false;
            _settingsManager.Save();
            
            Dispatcher.UIThread.Post(() => 
            {
                _hotkeyMenuItem.Header = $"Chang Hotkey (Code: {_settingsManager.CurrentHotkey})";
                _activePopup?.Close();
                _activePopup = null;
            });

            _soundManager.PlayNoti();
            return;
        }
        
        if (vKey != _settingsManager.CurrentHotkey) return;
        
        bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        
        bool ctrlMatch  = (isCtrlDown  == _settingsManager.RequireCtrl);
        bool shiftMatch = (isShiftDown == _settingsManager.RequireShift);
        bool altMatch   = (isAltDown   == _settingsManager.RequireAlt);

        if (ctrlMatch && shiftMatch && altMatch)
        {
            ToggleMicrophone();
        }
    }
    
    // Helper to filter keys that should not be triggers (Shift, Ctrl, Alt, Win)
    private bool IsModifierKey(int vk)
    {
        // 16=Shift, 17=Ctrl, 18=Alt, 91=LWin, 92=RWin, 160-165=L/R Shift/Ctrl/Alt
        return (vk >= 16 && vk <= 18) || (vk == 91 || vk == 92) || (vk >= 160 && vk <= 165);
    }

    private void ToggleMicrophone()
    {
        MicrophoneController.SetMicMuted(!MicrophoneController.IsMuted);
        microphoneStatusIcon.Source = MicrophoneController.IsMuted ? _mutedImg : _unmutedImg;
        if (MicrophoneController.IsMuted)
        {
            _soundManager.PlayMuted();
            _muteBtn.IsChecked = true;
            _trayIcon.Icon = _mutedIcon;
        }
        else
        {
            _soundManager.PlayUnmuted();
            _muteBtn.IsChecked = false;
            _trayIcon.Icon = _unmutedIcon;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _settingsManager.Save();
        _hiddenWindowInstance?.Stop();
        _soundManager?.Dispose();
        MicrophoneController.SetMicMuted(false);
        base.OnClosing(e);
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }
}
