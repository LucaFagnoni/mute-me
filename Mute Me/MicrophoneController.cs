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
    
    public static bool GetMicrophoneMuteStatus()
    {
        var deviceEnumerator = new MMDeviceEnumerator() as IMMDeviceEnumerator;
        IMMDevice speakers = null;
        IAudioEndpointVolume vol = null;

        try
        {
            deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eMultimedia, out speakers);
            
            Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
            speakers.Activate(ref IID_IAudioEndpointVolume, 0, IntPtr.Zero, out object o);
            vol = (IAudioEndpointVolume)o;
            
            vol.GetMute(out bool isMuted);
            
            IsMuted = isMuted; 
            
            return isMuted;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (vol != null) Marshal.ReleaseComObject(vol);
            if (speakers != null) Marshal.ReleaseComObject(speakers);
            if (deviceEnumerator != null) Marshal.ReleaseComObject(deviceEnumerator);
        }
    }

    #region Core Audio API Definitions
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void NotImpl1(); // RegisterControlChangeNotify
        void NotImpl2(); // UnregisterControlChangeNotify
        void NotImpl3(); // GetChannelCount
        void NotImpl4(); // SetMasterVolumeLevel
        void NotImpl5(); // SetMasterVolumeLevelScalar
        void NotImpl6(); // GetMasterVolumeLevel
        void NotImpl7(); // GetMasterVolumeLevelScalar
        void NotImpl8(); // SetChannelVolumeLevel
        void NotImpl9(); // SetChannelVolumeLevelScalar
        void NotImpl10(); // GetChannelVolumeLevel
        void NotImpl11(); // GetChannelVolumeLevelScalar
        void NotImpl12(); // SetMute
        [PreserveSig]
        int GetMute(out bool pbMute);
    }

    private enum EDataFlow
    {
        eRender,
        eCapture, // Microphone
        eAll,
        EDataFlow_enum_count
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications,
        ERole_enum_count
    }
    #endregion
}