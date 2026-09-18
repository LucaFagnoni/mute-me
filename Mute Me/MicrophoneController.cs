using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Mute_Me;

public static partial class MicrophoneController
{
    public static bool IsMuted;

    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private static readonly StrategyBasedComWrappers ComStrategy = new();
    private static IMMDeviceEnumerator? _enumerator;
    private static IAudioEndpointVolume? _volumeControl;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

    public static bool? GetMicrophoneMuteStatus()
    {
        // Due tentativi: il primo fallisce se l'endpoint in cache e' morto
        // (mic scollegato, cambio device di default), il secondo gira su uno appena creato.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var vol = Endpoint();
            if (vol == null) return null;
            try
            {
                if (vol.GetMute(out int muted) == 0)
                {
                    IsMuted = muted != 0;
                    return IsMuted;
                }
            }
            catch { }
            Reset();
        }
        return null;
    }

    public static void SetMicMuted(bool mute)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var vol = Endpoint();
            if (vol == null) return;
            try
            {
                if (vol.SetMute(mute ? 1 : 0, IntPtr.Zero) == 0)
                {
                    IsMuted = mute;
                    return;
                }
            }
            catch { }
            Reset();
        }
    }

    private static IAudioEndpointVolume? Endpoint()
    {
        if (_volumeControl != null) return _volumeControl;
        try
        {
            int hr = CoCreateInstance(CLSID_MMDeviceEnumerator, IntPtr.Zero, 23 /* CLSCTX_ALL */,
                IID_IMMDeviceEnumerator, out IntPtr ptrEnumerator);
            if (hr != 0 || ptrEnumerator == IntPtr.Zero) return null;

            _enumerator = (IMMDeviceEnumerator)ComStrategy.GetOrCreateObjectForComInstance(ptrEnumerator, CreateObjectFlags.None);

            // 1 = eCapture, 0 = eConsole
            if (_enumerator.GetDefaultAudioEndpoint(1, 0, out IMMDevice mic) != 0) { Reset(); return null; }

            Guid iid = IID_IAudioEndpointVolume;
            if (mic.Activate(ref iid, 0, IntPtr.Zero, out var vol) != 0) { Reset(); return null; }

            _volumeControl = vol;
        }
        catch { Reset(); }
        return _volumeControl;
    }

    private static void Reset()
    {
        _volumeControl = null;
        _enumerator = null;
    }
}

// --- INTERFACCE COM ---
// I NotImplN sono padding di vtable: servono a far cadere i metodi veri
// sullo slot giusto, non toccare l'ordine.

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
    void NotImpl9(); void NotImpl10(); void NotImpl11();

    // Slot 12
    [PreserveSig]
    int SetMute(int bMute, IntPtr pguidEventContext);

    // Slot 13
    [PreserveSig]
    int GetMute(out int pbMute);
}
