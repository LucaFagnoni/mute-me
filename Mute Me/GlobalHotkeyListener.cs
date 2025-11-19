using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Win32.Input;

public static class GlobalHotkeyListener
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;

    private static readonly LowLevelKeyboardProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;

    public static event Action<Key>? KeyPressed;

    public static IntPtr Start()
    {
        if (_hookID != IntPtr.Zero)
            return _hookID;

        _hookID = SetHook(_proc);
        return _hookID;
    }

    public static void Stop()
    {
        if (_hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
        }
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
            GetModuleHandle(curModule.ModuleName), 0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Estrarre lo scanCode da KBDLLHOOKSTRUCT
                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int scanCode = kbd.scanCode;
                
                // conversione corretta VK -> Avalonia.Key
                Key key = KeyInterop.KeyFromVirtualKey(vkCode, scanCode);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    KeyPressed?.Invoke(key);
                });
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText("hook_errors.txt", ex.ToString() + "\n");
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }


    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
}
