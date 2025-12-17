using System.Reflection;
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
    
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    
    // MODIFIER KEYS
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // ALT
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    
    private static Avalonia.Media.Imaging.Bitmap _mutedImg;
    private static Avalonia.Media.Imaging.Bitmap _unmutedImg;
    private static WindowIcon _mutedIcon;
    private static WindowIcon _unmutedIcon;
    
    private HiddenRawInputWindow _hiddenWindowInstance;
    private SettingsManager _settingsManager;
    private SoundManager _soundManager;
    private AutoStartManager _autoStartManager;
    
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }
    
    // MODIFIER REFERENCES
    private NativeMenuItem _modifierNone; 
    private NativeMenuItem _modifierShift; 
    private NativeMenuItem _modifierCtrl; 
    private NativeMenuItem _modifierAlt; 
    
    private bool _isRecording = false;
    private HotkeyPopup? _activePopup; 
    
    private NativeMenuItem _hotkeyMenuItem;
    private NativeMenuItem _muteBtn;
    private TrayIcon _trayIcon;
    
    
    private void LogToFile(string message)
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(desktop, "MuteMe_Debug.txt");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
        }
        catch { /* Niente da fare se fallisce il log */ }
    }
    
    public MainWindow()
    {
        InitializeComponent();
        
        LogToFile("=== AVVIO APPLICAZIONE ===");
        
        try 
        {
            _settingsManager = SettingsManager.Load();
            LogToFile("Settings caricati.");
        }
        catch(Exception ex) { LogToFile($"Errore Settings: {ex.Message}"); }
        
        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "Mute_Me";
        LogToFile($"Nome Assembly Rilevato: '{assemblyName}'");
        
        #region Load Images
        try 
        {
            // var uriMuted = new Uri("avares://MuteMe/Assets/muted.png");
            string uriMutedString = $"avares://{assemblyName}/Assets/muted.png";
            LogToFile($"Tentativo caricamento: {uriMutedString}");
            
            var uriMuted = new Uri(uriMutedString);
            using (var stream = AssetLoader.Open(uriMuted))
            {
                _mutedImg = new Avalonia.Media.Imaging.Bitmap(stream);
                LogToFile("-> Bitmap Muted OK");
            }
            using (var streamIcon = AssetLoader.Open(uriMuted))
            {
                _mutedIcon = new WindowIcon(streamIcon);
                LogToFile("-> Icon Muted OK");
            }
            
            // var uriUnmuted =new Uri("avares://MuteMe/Assets/unmuted.png");
            string uriUnmutedString = $"avares://{assemblyName}/Assets/unmuted.png";
            LogToFile($"Tentativo caricamento: {uriUnmutedString}");
            
            var uriUnmuted = new Uri(uriUnmutedString);
            using (var stream2 = AssetLoader.Open(uriUnmuted))
            {
                _unmutedImg = new Avalonia.Media.Imaging.Bitmap(stream2);
                LogToFile("-> Bitmap Unmuted OK");
            }
            using (var streamIcon2 = AssetLoader.Open(uriUnmuted))
            {
                _unmutedIcon = new WindowIcon(streamIcon2);
                LogToFile("-> Icon Unmuted OK");
            }
        }
        catch (Exception ex)
        {
            LogToFile($"!!! ERRORE ASSETS VISIVI: {ex.Message}");
            LogToFile($"StackTrace: {ex.StackTrace}");
            
            _mutedIcon = this.Icon;
            _unmutedIcon = this.Icon;
        }
        // using var stream = AssetLoader.Open(new Uri("avares://MuteMe/Assets/muted.png"));
        // _mutedImg = new Avalonia.Media.Imaging.Bitmap(stream);
        //
        // using var stream2 = AssetLoader.Open(new Uri("avares://MuteMe/Assets/unmuted.png"));
        // _unmutedImg = new Avalonia.Media.Imaging.Bitmap(stream2);
        //
        // _mutedIcon = new WindowIcon(stream);
        // _unmutedIcon = new WindowIcon(stream2);
        // stream.Dispose();
        // stream2.Dispose();
        #endregion
        
        #region Load Sounds
        try
        {
            LogToFile("Caricamento Suoni...");
            Uri uriMuteSnd = new Uri("avares://MuteMe/Assets/mute.wav");
            Uri uriUnmuteSnd = new Uri("avares://MuteMe/Assets/unmute.wav");
            Uri uriNotiSnd = new Uri("avares://MuteMe/Assets/notification.wav");
            
            _soundManager = new SoundManager(uriMuteSnd, uriUnmuteSnd, uriNotiSnd);
            _soundManager.Volume = _settingsManager.SfxVolume;
            LogToFile("SoundManager Inizializzato.");
        }
        catch (Exception ex)
        {
            LogToFile($"!!! ERRORE AUDIO: {ex.Message}");
        }
        #endregion
        
        try
        {
            _hiddenWindowInstance = new HiddenRawInputWindow();
            _hiddenWindowInstance.KeyPressed += OnRawKeyPressed;
            _hiddenWindowInstance.Start();
            LogToFile("RawInput Avviato.");
        }
        catch (Exception ex)
        {
            LogToFile($"Errore RawInput: {ex.Message}");
        }

        _autoStartManager = new AutoStartManager();
        SetupTrayIcon();
        LoadModifiers();
        
        Opened += (_, _) => EnableClickThrough();
        LogToFile("Costruttore completato. Nascondo finestra.");
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
        LogToFile("Inizio SetupTrayIcon...");
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
        LogToFile("Menu Volume e Mute aggiunti.");
        
        // 3. Modifiers Menu
        var modifierMenuItem = new NativeMenuItem("Modifiers");
        var modMenu = new NativeMenu();
        _modifierNone = new NativeMenuItem("None");
        _modifierNone.ToggleType = NativeMenuItemToggleType.Radio;
        _modifierNone.Click += (_, _) =>
        {
            ResetModifierRequirements();
            _settingsManager.Save();
        };
        _modifierShift = new NativeMenuItem("Shift");
        _modifierShift.ToggleType = NativeMenuItemToggleType.Radio;
        _modifierShift.Click += (_, _) =>
        {
            ResetModifierRequirements();
            _settingsManager.RequireShift =  true;
            _settingsManager.Save();
        }; 
        _modifierCtrl = new NativeMenuItem("Ctrl");
        _modifierCtrl.ToggleType = NativeMenuItemToggleType.Radio;
        _modifierCtrl.Click += (_, _) =>
        {
            ResetModifierRequirements();
            _settingsManager.RequireCtrl = true;
            _settingsManager.Save();
        }; 
        _modifierAlt = new NativeMenuItem("Alt");
        _modifierAlt.ToggleType = NativeMenuItemToggleType.Radio;
        _modifierAlt.Click += (_, _) =>
        {
            ResetModifierRequirements();
            _settingsManager.RequireAlt = true;
            _settingsManager.Save();
        }; 
        modMenu.Add(_modifierNone);
        modMenu.Add(_modifierShift);
        modMenu.Add(_modifierCtrl);
        modMenu.Add(_modifierAlt);
        modifierMenuItem.Menu = modMenu;
        menu.Add(modifierMenuItem);
        LogToFile("Menu Modifiers aggiunto.");
        
        // 4. Set Hotkey Button
        _hotkeyMenuItem = new NativeMenuItem($"Set Hotkey (Current: {_settingsManager.CurrentHotkey})");
        _hotkeyMenuItem.Click += (_, _) => StartRecordingHotkey();
        menu.Add(_hotkeyMenuItem);
        LogToFile("Menu Hotkey aggiunto.");
        
        // 5. Set Admin Auto Start with Windows
        var autoStartItem = new NativeMenuItem("Loading...");
        autoStartItem.ToggleType = NativeMenuItemToggleType.CheckBox;
        LogToFile("Chiamata InitializeAutoStartItem...");
        InitializeAutoStartItem(autoStartItem);
        autoStartItem.Click += async (_, _) =>
        {
            autoStartItem.Header = "Working...";
            autoStartItem.IsEnabled = false; 

            bool isActive = await _autoStartManager.IsStartupTaskActiveAsync();

            if (isActive)
            {
                await _autoStartManager.UninstallStartupTaskAsync();
            }
            else
            {
                await _autoStartManager.InstallStartupTaskAsync();
            }
            
            bool nowActive = await _autoStartManager.IsStartupTaskActiveAsync();
            
            Dispatcher.UIThread.Post(() => 
            {
                autoStartItem.IsChecked = nowActive;
                autoStartItem.Header = "Auto Start (Admin)";
                autoStartItem.IsEnabled = true; 
            });
        };
        menu.Add(autoStartItem);

        // 6. Separator
        menu.Add(new NativeMenuItemSeparator());
        
        // 7. Uscita
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => Environment.Exit(0);
        menu.Add(exitItem);

        _trayIcon.Menu = menu;
        _trayIcon.IsVisible = true;
        #endregion
    }
    
    private async void InitializeAutoStartItem(NativeMenuItem item)
    {
        try
        {
            LogToFile("Dentro InitializeAutoStartItem: Chiamata IsStartupTaskActiveAsync...");
            bool active = await _autoStartManager.IsStartupTaskActiveAsync();
            LogToFile($"IsStartupTaskActiveAsync completato. Risultato: {active}");
            
            Dispatcher.UIThread.Post(() => 
            {
                item.IsChecked = active;
                item.Header = "Auto Start (Admin)";
            });
        }
        catch (Exception ex)
        {
            LogToFile($"!!! CRASH IN AUTOSTART INIT: {ex.Message}");
            LogToFile(ex.StackTrace);
        }
    }
    
    private void ResetModifierRequirements()
    {
        _settingsManager.RequireShift = false;
        _settingsManager.RequireCtrl = false;
        _settingsManager.RequireAlt = false;
    }

    private void LoadModifiers()
    {
        if(_settingsManager is {RequireShift: false, RequireCtrl: false, RequireAlt: false}) _modifierNone.IsChecked = true;
        if(_settingsManager.RequireShift) _modifierShift.IsChecked = true;
        if(_settingsManager.RequireCtrl) _modifierCtrl.IsChecked = true;
        if(_settingsManager.RequireAlt) _modifierAlt.IsChecked = true;
    }
    
    private void StartRecordingHotkey()
    {
        // Avoids double-clicking
        if (_isRecording) return;

        _isRecording = true;
        _soundManager.PlayNoti();

        // Show hotkey recording window
        // Dispatcher.UIThread.Post ensures the UI is created on the correct thread
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
            _soundManager.Volume = _settingsManager.SfxVolume;
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
                _hotkeyMenuItem.Header = $"Cambia Hotkey (Codice: {_settingsManager.CurrentHotkey})";

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
    
    private async void CheckAutoStartStatus(NativeMenuItem item)
    {
        bool active = await _autoStartManager.IsStartupTaskActiveAsync();
        // Aggiorna UI
        Dispatcher.UIThread.Post(() => 
        {
            item.Header = active ? "✔ Avvio Automatico (Admin)" : "Avvio Automatico (Admin)";
        });
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _settingsManager.Save();
        _hiddenWindowInstance?.Stop();
        _soundManager?.Dispose();
        MicrophoneController.SetMicMuted(false);
        base.OnClosing(e);
    }
}