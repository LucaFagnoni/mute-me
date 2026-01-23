using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling; // Fondamentale per il nuovo COM

namespace Mute_Me;

public static partial class MicrophoneController
{
    public static bool IsMuted = false;

    // --- P/INVOKE ---
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();
    
    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    // GUIDs
    private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

    // CACHED COM OBJECTS
    private static IMMDeviceEnumerator? _enumerator;
    private static IAudioEndpointVolume? _volumeControl;
    private static StrategyBasedComWrappers _comStrategy = new StrategyBasedComWrappers();

    // CoCreateInstance that return raw IntPtr
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    public static bool? GetMicrophoneMuteStatus()
    {
        try
        {
            if (_volumeControl == null)
            {
                InitializeComObjects();
            }

            if (_volumeControl == null) return null;

            // 5. Read State
            _volumeControl.GetMute(out int isMutedInt);
            
            bool isMuted = isMutedInt != 0;
            IsMuted = isMuted;
            
            return isMuted;
        }
        catch
        {
            // If it fails, force re-initialization next time
            _volumeControl = null;
            _enumerator = null;
            return null;
        }
    }

    private static void InitializeComObjects()
    {
        try 
        {
             // 1. Create raw COM instance (IntPtr)
            int hr = CoCreateInstance(
                CLSID_MMDeviceEnumerator, 
                IntPtr.Zero, 
                23, // CLSCTX_ALL
                IID_IMMDeviceEnumerator, 
                out IntPtr ptrEnumerator);

            if (hr != 0 || ptrEnumerator == IntPtr.Zero) return;

            // 2. Wrap the pointer
            _enumerator = (IMMDeviceEnumerator)_comStrategy.GetOrCreateObjectForComInstance(ptrEnumerator, CreateObjectFlags.None);

            // 3. Obtain Device (1 = eCapture, 0 = eConsole)
            _enumerator.GetDefaultAudioEndpoint(1, 0, out IMMDevice speakers);

            // 4. Activate Volume
            Guid iid = IID_IAudioEndpointVolume;
            speakers.Activate(ref iid, 0, IntPtr.Zero, out var vol);
            _volumeControl = vol;
        }
        catch 
        {
            _volumeControl = null;
            _enumerator = null; 
        }
    }

    public static void SetMicMuted(bool mute)
    {
        bool? realState = GetMicrophoneMuteStatus();
        
        if (realState.HasValue && realState.Value == mute)
        {
            IsMuted = mute;
            return;
        }

        try
        {
            IntPtr h = GetForegroundWindow();
            SendMessageW(h, 0x319, IntPtr.Zero, (IntPtr)0x180000);
            IsMuted = mute;
        }
        catch { }
    }
}

// --- INTERFACCE COM (Definite con il nuovo sistema) ---

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IMMDeviceEnumerator
{
    void NotImpl1();
    
    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IAudioEndpointVolume ppInterface);
}

[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IAudioEndpointVolume
{
    void NotImpl1(); void NotImpl2(); void NotImpl3(); void NotImpl4(); 
    void NotImpl5(); void NotImpl6(); void NotImpl7(); void NotImpl8(); 
    void NotImpl9(); void NotImpl10(); void NotImpl11(); void NotImpl12(); 
    
    [PreserveSig]
    int GetMute(out int pbMute);
}