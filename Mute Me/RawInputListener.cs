// using System;
// using System.Runtime.InteropServices;
// using System.Threading;
// using Avalonia.Input;
//
// public static class RawInputListener
// {
//     private const int WS_EX_TOOLWINDOW = 0x00000080;
//     private const int WS_EX_NOACTIVATE = 0x08000000;
//     private const int WS_POPUP = unchecked((int)0x80000000);
//
//     private const int WM_INPUT = 0x00FF;
//     private const int RID_INPUT = 0x10000003;
//     private const int RIDEV_INPUTSINK = 0x00000100;
//
//     private static IntPtr _hwnd;
//
//     public static event Action<Key>? KeyPressed;
//
//     private static Thread? _messageThread;
//
//     public static void Start()
//     {
//         if (_messageThread != null)
//             return;
//
//         _messageThread = new Thread(MessageThreadProc)
//         {
//             IsBackground = true
//         };
//         _messageThread.Start();
//     }
//
//     private static void MessageThreadProc()
//     {
//         // 1. Registriamo la classe finestra
//         WNDCLASS wc = new WNDCLASS
//         {
//             lpfnWndProc = WndProc,
//             lpszClassName = "RawInputHiddenWindow"
//         };
//
//         ushort classAtom = RegisterClass(ref wc);
//
//         // 2. Creiamo una finestra invisibile che riceve RAWINPUT
//         _hwnd = CreateWindowEx(
//             WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
//             classAtom,
//             "",
//             WS_POPUP,
//             0, 0, 0, 0,
//             IntPtr.Zero,
//             IntPtr.Zero,
//             IntPtr.Zero,
//             IntPtr.Zero
//         );
//
//         RegisterForRawInput(_hwnd);
//
//         // 3. Loop dei messaggi
//         MSG msg;
//         while (GetMessage(out msg, IntPtr.Zero, 0, 0))
//         {
//             TranslateMessage(ref msg);
//             DispatchMessage(ref msg);
//         }
//     }
//
//     private static void RegisterForRawInput(IntPtr hwnd)
//     {
//         RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
//
//         rid[0].usUsagePage = 0x01; // generic desktop
//         rid[0].usUsage = 0x06;     // keyboard
//         rid[0].dwFlags = RIDEV_INPUTSINK;
//         rid[0].hwndTarget = hwnd;
//
//         if (!RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
//             throw new Exception("RegisterRawInputDevices fallita.");
//     }
//
//     private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
//     {
//         if (msg == WM_INPUT)
//             ProcessRawInput(lParam);
//
//         return DefWindowProc(hwnd, msg, wParam, lParam);
//     }
//
//     private static void ProcessRawInput(IntPtr hRawInput)
//     {
//         uint dwSize = 0;
//         GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
//
//         IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
//
//         try
//         {
//             if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == dwSize)
//             {
//                 RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(buffer)!;
//
//                 if (raw.header.dwType == 1) // keyboard
//                 {
//                     int vk = raw.keyboard.VKey;
//
//                     // Evitiamo tasti fantasma
//                     if (vk != 0)
//                     {
//                         var key = KeyInterop.KeyFromVirtualKey(vk);
//                         KeyPressed?.Invoke(key);
//                     }
//                 }
//             }
//         }
//         finally
//         {
//             Marshal.FreeHGlobal(buffer);
//         }
//     }
//
//     #region Native
//
//     [StructLayout(LayoutKind.Sequential)]
//     private struct RAWINPUTDEVICE
//     {
//         public ushort usUsagePage;
//         public ushort usUsage;
//         public int dwFlags;
//         public IntPtr hwndTarget;
//     }
//
//     [StructLayout(LayoutKind.Sequential)]
//     private struct RAWINPUTHEADER
//     {
//         public uint dwType;
//         public uint dwSize;
//         public IntPtr hDevice;
//         public IntPtr wParam;
//     }
//
//     [StructLayout(LayoutKind.Explicit)]
//     private struct RAWINPUT
//     {
//         [FieldOffset(0)] public RAWINPUTHEADER header;
//         [FieldOffset(16)] public RAWKEYBOARD keyboard;
//     }
//
//     [StructLayout(LayoutKind.Sequential)]
//     private struct RAWKEYBOARD
//     {
//         public ushort MakeCode;
//         public ushort Flags;
//         public ushort Reserved;
//         public ushort VKey;
//         public uint Message;
//         public uint ExtraInformation;
//     }
//
//     [StructLayout(LayoutKind.Sequential)]
//     private struct MSG
//     {
//         public IntPtr hwnd;
//         public uint message;
//         public IntPtr wParam;
//         public IntPtr lParam;
//         public uint time;
//         public int pt_x;
//         public int pt_y;
//     }
//
//     [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
//     private struct WNDCLASS
//     {
//         public uint style;
//         public WndProcDelegate lpfnWndProc;
//         public int cbClsExtra;
//         public int cbWndExtra;
//         public IntPtr hInstance;
//         public string lpszClassName;
//     }
//
//     private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
//
//     [DllImport("user32.dll", CharSet = CharSet.Auto)]
//     private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
//
//     [DllImport("user32.dll", CharSet = CharSet.Auto)]
//     private static extern IntPtr CreateWindowEx(
//         int dwExStyle,
//         ushort lpClassName,
//         string lpWindowName,
//         int dwStyle,
//         int x, int y,
//         int nWidth, int nHeight,
//         IntPtr hWndParent,
//         IntPtr hMenu,
//         IntPtr hInstance,
//         IntPtr lpParam);
//
//     [DllImport("user32.dll")]
//     private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
//
//     [DllImport("user32.dll")]
//     private static extern bool TranslateMessage(ref MSG lpMsg);
//
//     [DllImport("user32.dll")]
//     private static extern IntPtr Dispatch
