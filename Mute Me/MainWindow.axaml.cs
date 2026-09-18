using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Mute_Me;

public partial class MainWindow : Window
{
    // WINDOW STYLES
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    // MODIFIER KEYS
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // ALT

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // MANAGERS
    private readonly SettingsManager _settingsManager;
    private readonly SoundManager _soundManager;
    private readonly AutoStartManager _autoStartManager = new();
    private readonly HiddenRawInputWindow _hiddenWindowInstance;

    // UI RESOURCES
    private readonly Bitmap? _mutedImg, _unmutedImg;
    private readonly WindowIcon? _mutedIcon, _unmutedIcon;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _hotkeyMenuItem;
    private NativeMenuItem? _muteBtn;

    // STATE
    private bool _isRecording;
    private HotkeyPopup? _activePopup;
    private WinEventProc? _winEventProc; // reference tenuta viva per l'hook
    private IntPtr _winEventHook;

    public MainWindow()
    {
        InitializeComponent();

        _settingsManager = SettingsManager.Load();

        const string uriMuted = "avares://MuteMe/Assets/muted.png";
        const string uriUnmuted = "avares://MuteMe/Assets/unmuted.png";
        _mutedImg = LoadBitmap(uriMuted);
        _unmutedImg = LoadBitmap(uriUnmuted);
        _mutedIcon = LoadIcon(uriMuted);
        _unmutedIcon = LoadIcon(uriUnmuted);

        _soundManager = new SoundManager(
            new Uri("avares://MuteMe/Assets/mute.wav"),
            new Uri("avares://MuteMe/Assets/unmute.wav"),
            new Uri("avares://MuteMe/Assets/notification.wav"))
        {
            Volume = _settingsManager.SfxVolume
        };

        _hiddenWindowInstance = new HiddenRawInputWindow();
        _hiddenWindowInstance.KeyPressed += OnRawKeyPressed;
        _hiddenWindowInstance.Start();

        SetupTrayIcon();
        SetMicrophone(MicrophoneController.GetMicrophoneMuteStatus() ?? false);

        Opened += (_, _) =>
        {
            EnableClickThrough();
            EnforceTopmost();
            HookForegroundChanges();
        };

        UpdateIconPosition();
    }

    // Invece di ripassare SetWindowPos ogni 2 secondi, ci fa svegliare Windows quando
    // qualcun altro prende il foreground: e' l unico momento in cui possiamo finire
    // sotto un altra finestra.
    // ponytail: solo eventi di foreground. Un app che va topmost senza prendere il
    // foreground (certi overlay di gioco) ci copre ancora: in quel caso aggiungere
    // EVENT_OBJECT_SHOW (0x8002) allo stesso hook.
    private void HookForegroundChanges()
    {
        _winEventProc = (_, _, _, _, _, _, _) => EnforceTopmost();
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void EnforceTopmost()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void EnableClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void UpdateIconPosition()
    {
        if (Screens.Primary == null) return;

        var screen = Screens.Primary.WorkingArea;
        // Window size is 10x10 logical. Convert to pixels.
        var scaling = Screens.Primary.Scaling;
        int w = (int)(Width * scaling);
        int h = (int)(Height * scaling);
        int m = _settingsManager.IconMargin;

        int x = 0, y = 0;

        switch (_settingsManager.IconCorner)
        {
            case 0: // Top-Left
                x = screen.X + m;
                y = screen.Y + m;
                break;
            case 1: // Top-Right
                x = screen.X + screen.Width - w - m;
                y = screen.Y + m;
                break;
            case 2: // Bottom-Left
                x = screen.X + m;
                y = screen.Y + screen.Height - h - m;
                break;
            case 3: // Bottom-Right
                x = screen.X + screen.Width - w - m;
                y = screen.Y + screen.Height - h - m;
                break;
        }

        Position = new PixelPoint(x, y);
    }

    private WindowIcon? LoadIcon(string uri)
    {
        try
        {
            using var s = AssetLoader.Open(new Uri(uri));
            return new WindowIcon(s);
        }
        catch { return null; }
    }

    private Bitmap? LoadBitmap(string uri)
    {
        try
        {
            using var s = AssetLoader.Open(new Uri(uri));
            return new Bitmap(s);
        }
        catch { return null; }
    }

    private void SetupTrayIcon()
    {
        _trayIcon ??= new TrayIcon
        {
            Icon = _unmutedIcon,
            ToolTipText = "Mute Me"
        };

        var menu = new NativeMenu();

        // 1. SFX Volume
        var volumeItem = new NativeMenuItem("Sfx Volume");
        volumeItem.Click += (_, _) => OpenVolumePopup();
        menu.Add(volumeItem);

        // 2. Mute Button
        _muteBtn = new NativeMenuItem("Mute")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = MicrophoneController.IsMuted
        };
        _muteBtn.Click += (_, _) => SetMicrophone(!MicrophoneController.IsMuted);
        menu.Add(_muteBtn);

        // 3. Modifiers Submenu
        var modifierMenuItem = new NativeMenuItem("Modifiers");
        var modMenu = new NativeMenu();
        modMenu.Add(CreateRadioItem("None", () => SetModifier(false, false, false), _settingsManager is { RequireShift: false, RequireCtrl: false, RequireAlt: false }));
        modMenu.Add(CreateRadioItem("Shift", () => SetModifier(true, false, false), _settingsManager.RequireShift));
        modMenu.Add(CreateRadioItem("Ctrl", () => SetModifier(false, true, false), _settingsManager.RequireCtrl));
        modMenu.Add(CreateRadioItem("Alt", () => SetModifier(false, false, true), _settingsManager.RequireAlt));
        modifierMenuItem.Menu = modMenu;
        menu.Add(modifierMenuItem);

        // 4. Icon Position
        var posMenuItem = new NativeMenuItem("Icon Position");
        var posMenu = new NativeMenu();

        posMenu.Add(CreateRadioItem("Top Left", () => SetCorner(0), _settingsManager.IconCorner == 0));
        posMenu.Add(CreateRadioItem("Top Right", () => SetCorner(1), _settingsManager.IconCorner == 1));
        posMenu.Add(CreateRadioItem("Bottom Left", () => SetCorner(2), _settingsManager.IconCorner == 2));
        posMenu.Add(CreateRadioItem("Bottom Right", () => SetCorner(3), _settingsManager.IconCorner == 3));

        posMenu.Add(new NativeMenuItemSeparator());

        var marginItem = new NativeMenuItem($"Margin ({_settingsManager.IconMargin}px)");
        marginItem.Click += (_, _) => OpenMarginPopup();
        posMenu.Add(marginItem);

        posMenuItem.Menu = posMenu;
        menu.Add(posMenuItem);

        // 5. Set Hotkey Button
        _hotkeyMenuItem = new NativeMenuItem(HotkeyLabel());
        _hotkeyMenuItem.Click += (_, _) => StartRecordingHotkey();
        menu.Add(_hotkeyMenuItem);

        // 6. Auto Start with Windows
        var autoStartItem = new NativeMenuItem("Loading...");
        InitializeAutoStartItem(autoStartItem);
        menu.Add(autoStartItem);

        menu.Add(new NativeMenuItemSeparator());

        // 7. Exiting
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            Shutdown();
            Environment.Exit(0);
        };
        menu.Add(exitItem);

        _trayIcon.Menu = menu;
        _trayIcon.IsVisible = true;
    }

    private string HotkeyLabel() =>
        $"Set Hotkey (Current: {KeyNames.Describe(_settingsManager.HotkeyScanCode, _settingsManager.HotkeyExtended)})";

    private NativeMenuItem CreateRadioItem(string text, Action action, bool isChecked)
    {
        var item = new NativeMenuItem(text) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetModifier(bool shift, bool ctrl, bool alt)
    {
        _settingsManager.RequireShift = shift;
        _settingsManager.RequireCtrl = ctrl;
        _settingsManager.RequireAlt = alt;
        _settingsManager.Save();
    }

    private void SetCorner(int corner)
    {
        _settingsManager.IconCorner = corner;
        _settingsManager.Save();
        UpdateIconPosition();
        SetupTrayIcon(); // il menu nativo non rinfresca i check da solo
    }

    private void OpenVolumePopup()
    {
        var popup = new SliderPopup("Effects Volume", _settingsManager.SfxVolume, 10, "%");
        popup.ValueChanged += v =>
        {
            _settingsManager.SfxVolume = v;
            _soundManager.Volume = v;
            _settingsManager.Save();
        };
        ShowNearCursor(popup);
    }

    private void OpenMarginPopup()
    {
        var popup = new SliderPopup("Icon Margin (px)", _settingsManager.IconMargin, 1, "");
        popup.ValueChanged += v =>
        {
            _settingsManager.IconMargin = v;
            _settingsManager.Save();
            UpdateIconPosition();
        };
        // Il testo "Margin (Xpx)" si aggiorna una volta sola alla chiusura,
        // non a ogni tick dello slider.
        popup.Closed += (_, _) => SetupTrayIcon();
        ShowNearCursor(popup);
    }

    private void ShowNearCursor(Window popup)
    {
        if (GetCursorPos(out POINT p))
            popup.Position = new PixelPoint(p.X - 100, p.Y - 60);

        popup.Show();
        popup.Activate();
    }

    private async void InitializeAutoStartItem(NativeMenuItem item)
    {
        item.ToggleType = NativeMenuItemToggleType.CheckBox;

        UpdateAutoStartItem(item, await _autoStartManager.IsStartupTaskActiveAsync());

        item.Click += async (_, _) =>
        {
            item.IsEnabled = false;
            item.Header = "Working...";

            if (await _autoStartManager.IsStartupTaskActiveAsync())
                await _autoStartManager.UninstallStartupTaskAsync();
            else
                await _autoStartManager.InstallStartupTaskAsync();

            UpdateAutoStartItem(item, await _autoStartManager.IsStartupTaskActiveAsync());
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
        _soundManager.Play(Sfx.Noti);

        Dispatcher.UIThread.Post(() =>
        {
            _activePopup = new HotkeyPopup();
            _activePopup.Canceled += () => _isRecording = false;
            _activePopup.Show();
        });
    }

    private void OnRawKeyPressed(int scanCode, bool extended)
    {
        if (_isRecording)
        {
            if (IsModifierKey(scanCode)) return; // Ignores CTRL/ALT alone

            _settingsManager.HotkeyScanCode = scanCode;
            _settingsManager.HotkeyExtended = extended;
            _isRecording = false;
            _settingsManager.Save();

            Dispatcher.UIThread.Post(() =>
            {
                if (_hotkeyMenuItem != null) _hotkeyMenuItem.Header = HotkeyLabel();
                _activePopup?.Close();
                _activePopup = null;
            });

            _soundManager.Play(Sfx.Noti);
            return;
        }

        if (scanCode != _settingsManager.HotkeyScanCode || extended != _settingsManager.HotkeyExtended) return;

        bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

        if (isCtrlDown == _settingsManager.RequireCtrl &&
            isShiftDown == _settingsManager.RequireShift &&
            isAltDown == _settingsManager.RequireAlt)
        {
            SetMicrophone(!MicrophoneController.IsMuted);
        }
    }

    // Scancode dei modificatori, uguali su ogni layout perche' sono posizioni
    // fisiche: Ctrl, LShift, RShift, Alt (E0 li rende i gemelli di destra),
    // piu' i tasti Windows e Menu.
    private bool IsModifierKey(int scanCode) =>
        scanCode is 0x1D or 0x2A or 0x36 or 0x38 or 0x5B or 0x5C or 0x5D;

    private void SetMicrophone(bool isMuted)
    {
        MicrophoneController.SetMicMuted(isMuted);

        bool muted = MicrophoneController.IsMuted;
        microphoneStatusIcon.Source = muted ? _mutedImg : _unmutedImg;
        microphoneStatusIcon.Opacity = muted ? 0.8 : 0.25;
        _soundManager.Play(muted ? Sfx.Mute : Sfx.Unmute);

        if (_muteBtn != null) _muteBtn.IsChecked = muted;
        if (_trayIcon != null) _trayIcon.Icon = muted ? _mutedIcon : _unmutedIcon;
    }

    private void Shutdown()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        _settingsManager.Save();
        _hiddenWindowInstance.Stop();
        _soundManager.Dispose();
        MicrophoneController.SetMicMuted(false);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Shutdown();
        base.OnClosing(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }
}
