using System;
using System.Runtime.InteropServices;

public class HiddenRawInputWindow
{
    private const uint WM_INPUT = 0x00FF;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RID_INPUT = 0x10000003;
    private const int WS_POPUP = unchecked((int)0x80000000);

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isRegistered;
    private WndProcDelegate _wndProc; // salva riferimento per il GC

    public event Action<int>? KeyPressed;

    private string _className = "HiddenRawInputWindow_Class";

    // ----------------------
    // START
    // ----------------------
    public void Start()
    {
        Console.WriteLine("HiddenRawInputWindow.Start()");

        // salva delegato
        _wndProc = WndProc;

        RegisterWindowClass();
        CreateHiddenWindow();

        if (_hwnd != IntPtr.Zero)
        {
            RegisterForRawInput(_hwnd);
        }
    }

    // ----------------------
    // STOP
    // ----------------------
    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DeregisterRawInput();
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        UnregisterClass(_className, GetModuleHandle(null));
        Console.WriteLine("HiddenRawInputWindow.Stop() completed.");
    }

    // ----------------------
    // WINDOW CLASS
    // ----------------------
    private void RegisterWindowClass()
    {
        WNDCLASSEX wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = _className
        };

        ushort atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            throw new Exception("RegisterClassEx failed, error=" + err);
        }

        Console.WriteLine("RegisterClassEx OK");
    }

    // ----------------------
    // HIDDEN WINDOW
    // ----------------------
    private void CreateHiddenWindow()
    {
        _hwnd = CreateWindowEx(
            0,
            _className,
            "",
            WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero
        );

        if (_hwnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine("CreateWindowEx FAILED, error=" + err);
            return;
        }

        Console.WriteLine("Hidden window created: " + _hwnd);
    }

    // ----------------------
    // RAW INPUT
    // ----------------------
    private void RegisterForRawInput(IntPtr hwnd)
    {
        RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
        rid[0].usUsagePage = 0x01; // generic desktop controls
        rid[0].usUsage = 0x06;     // keyboard
        rid[0].dwFlags = RIDEV_INPUTSINK;
        rid[0].hwndTarget = hwnd;

        if (!RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
        {
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine("RegisterRawInputDevices FAILED: " + err);
        }
        else
        {
            _isRegistered = true;
            Console.WriteLine("RegisterRawInputDevices OK");
        }
    }

    private void DeregisterRawInput()
    {
        if (!_isRegistered) return;

        RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
        rid[0].usUsagePage = 0x01;
        rid[0].usUsage = 0x06;
        rid[0].dwFlags = 0x00000001; // RIDEV_REMOVE
        rid[0].hwndTarget = IntPtr.Zero;

        RegisterRawInputDevices(rid, (uint)rid.Length,
            (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));

        _isRegistered = false;
    }

    // ----------------------
    // WNDPROC
    // ----------------------
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
        uint dwSize = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
        if (dwSize == 0) return;

        IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) != dwSize)
                return;

            RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(buffer);

            if (raw.header.dwType == 1) // keyboard
            {
                int vk = raw.keyboard.VKey;
                Console.WriteLine("Raw key: " + vk);
                KeyPressed?.Invoke(vk);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ----------------------
    // STRUCTS & WINAPI
    // ----------------------
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
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
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr hInstance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
