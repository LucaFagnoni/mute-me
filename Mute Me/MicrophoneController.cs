using System;
using System.Runtime.InteropServices;

namespace Mute_Me;

public static class MicrophoneController
{
    public static bool IsMuted = false;

    // P/Invoke manuale per CoCreateInstance (Bypassa il problema del costruttore AOT)
    [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void CoCreateInstance(
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    // CLSID di MMDeviceEnumerator
    private static Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
    // IID di IMMDeviceEnumerator
    private static Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
    
    private const uint CLSCTX_ALL = 23; // Supporta server in-process, locali, ecc.

    public static bool GetMicrophoneMuteStatus()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? speakers = null;
        IAudioEndpointVolume? vol = null;

        try
        {
            // 1. Creiamo l'istanza COM manualmente senza usare 'new'
            CoCreateInstance(
                CLSID_MMDeviceEnumerator, 
                IntPtr.Zero, 
                CLSCTX_ALL, 
                IID_IMMDeviceEnumerator, 
                out object objEnumerator);
            
            enumerator = (IMMDeviceEnumerator)objEnumerator;

            // 2. Ottieni il dispositivo di cattura
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eMultimedia, out speakers);

            // 3. Attiva l'interfaccia Volume
            Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
            speakers.Activate(ref IID_IAudioEndpointVolume, 0, IntPtr.Zero, out object o);
            vol = (IAudioEndpointVolume)o;

            // 4. Leggi stato
            vol.GetMute(out bool isMuted);
            
            // Allinea stato locale
            IsMuted = isMuted; 
            return isMuted;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (vol != null) Marshal.ReleaseComObject(vol);
            if (speakers != null) Marshal.ReleaseComObject(speakers);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    public static void SetMicMuted(bool mute)
    {
        // ... (Il resto del metodo SetMicMuted rimane invariato o puoi usare la logica COM sopra per settare il mute) ...
        // Per ora manteniamo la tua logica SendMessageW se funzionava, altrimenti 
        // puoi usare vol.SetMute(mute, Guid.Empty) nell'interfaccia sopra.
        
        try
        {
            if (IsMuted == mute) return;
            // Usa SendMessageW esistente...
            IntPtr h = GetForegroundWindow();
            SendMessageW(h, 0x319, IntPtr.Zero, (IntPtr)0x180000);
            IsMuted = mute;
        }
        catch { }
    }

    // --- P/INVOKE PER SENDMESSAGE (Il tuo codice vecchio) ---
    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    // --- INTERFACCE COM (Necessarie per GetMicrophoneMuteStatus) ---
    // NOTA: Usa 'internal' per AOT, non private
    
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        void NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        void NotImpl1(); void NotImpl2(); void NotImpl3(); void NotImpl4(); void NotImpl5(); 
        void NotImpl6(); void NotImpl7(); void NotImpl8(); void NotImpl9(); void NotImpl10(); 
        void NotImpl11(); 
        
        void SetMute([In] bool bMute, [In] Guid pguidEventContext); // Metodo aggiunto per completezza
        
        [PreserveSig]
        int GetMute(out bool pbMute);
    }

    internal enum EDataFlow { eRender, eCapture, eAll }
    internal enum ERole { eConsole, eMultimedia, eCommunications }
}