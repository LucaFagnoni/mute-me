using System.Runtime.InteropServices;

namespace Mute_Me;

public sealed class HiddenRawInputWindow
{
    // Consts
    private const uint WM_INPUT = 0x00FF;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RID_INPUT = 0x10000003;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const string CLASS_NAME = "HiddenRawInputWindow_Class_Unicode";
    
    // Static
    private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    // Fields
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isRegistered;
    private WndProcDelegate? _wndProc; // Reference kept to prevent GC collection

    // Events
    public event Action<int>? KeyPressed;

    public void Start()
    {
        _wndProc = WndProc; // Keep delegate alive

        try
        {
            RegisterWindowClass();
            CreateHiddenWindow();

            if (_hwnd != IntPtr.Zero)
            {
                RegisterForRawInput(_hwnd);
            }
        }
        catch 
        {
            // Fail silently
        }
    }
    
    public void Stop()
    {
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                DeregisterRawInput();
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            
            UnregisterClass(CLASS_NAME, GetModuleHandle(null));
        }
        catch 
        {
            // Fail silently
        }
    }
    
    
    private void RegisterWindowClass()
    {
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc!,
            hInstance = GetModuleHandle(null),
            lpszClassName = CLASS_NAME
        };

        ushort atom = RegisterClassEx(ref wc);

        // If atom is 0, there was an error. If the error is not “Class Already Exists,” we throw an exception.
        if (atom == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
        {
            throw new Exception("Failed to register window class.");
        }
    }
    
    private void CreateHiddenWindow()
    {
        _hwnd = CreateWindowEx(
            0,
            CLASS_NAME,
            "HiddenRawInputHost",
            0, 
            0, 0, 0, 0,
            HWND_MESSAGE, 
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero
        );
    }
    
    private void RegisterForRawInput(IntPtr hwnd)
    {
        var rid = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01, // Generic Desktop Controls
            usUsage = 0x06,     // Keyboard
            dwFlags = RIDEV_INPUTSINK,
            hwndTarget = hwnd
        };

        if (RegisterRawInputDevices(new[] { rid }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            _isRegistered = true;
        }
    }

    private void DeregisterRawInput()
    {
        if (!_isRegistered) return;

        var rid = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x06,
            dwFlags = 0x00000001, // RIDEV_REMOVE
            hwndTarget = IntPtr.Zero
        };

        RegisterRawInputDevices(new[] { rid }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        _isRegistered = false;
    }
    
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            ProcessRawInput(lParam);
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ProcessRawInput(IntPtr lParam)
    {
        // 1. Get Buffer Size
        uint dwSize = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, headerSize);

        if (dwSize == 0) return;

        // 2. Allocate & Read
        IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, headerSize) != dwSize)
                return;

            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);

            // 3. Process Keyboard Event
            if (raw.header.dwType == 1) // 1 = RIM_TYPEKEYBOARD
            {
                // Flags & 1 == 0 means Key Down (Make Code)
                bool isKeyDown = (raw.keyboard.Flags & 1) == 0; 
                
                if (isKeyDown)
                {
                    KeyPressed?.Invoke(raw.keyboard.VKey);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    #region Win32 API
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWKEYBOARD keyboard;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    #endregion
}