using System.Runtime.InteropServices;

public static class MicrophoneController
{
    public static bool IsMuted = false;
    
    [DllImport("user32.dll", SetLastError = false)]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = false)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    
    private const int WM_APPCOMMAND = 0x319;
    private const int APPCOMMAND_MICROPHONE_VOLUME_MUTE = 0x180000;
    
    public static void SetMicMuted(bool mute)
    {
        try
        {
            if (IsMuted == mute) return;
            
            IntPtr h = GetForegroundWindow();
            SendMessageW(h, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)APPCOMMAND_MICROPHONE_VOLUME_MUTE);
            IsMuted = mute;
        }
        catch (Exception ex)
        {
            // ignored
        }
    }
}