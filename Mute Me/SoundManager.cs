using System.Runtime.InteropServices;
using Avalonia.Platform;

public class SoundManager : IDisposable
{
    [DllImport("winmm.dll")] private static extern bool PlaySound(IntPtr p, IntPtr h, uint f);
    private const uint FLAGS = 0x0004 | 0x0001 | 0x0002; // MEMORY | ASYNC | NODEFAULT

    private byte[] _rawMute, _rawUnmute, _rawNoti;
    private IntPtr _pMute, _pUnmute, _pNoti;
    private float _volume = 1.0f;

    public int Volume
    {
        get => (int)(_volume * 100);
        set { _volume = Math.Clamp(value, 0, 100) / 100f; RefreshBuffers(); }
    }

    public SoundManager(Uri mute, Uri unmute, Uri noti)
    {
        _rawMute = Load(mute);
        _rawUnmute = Load(unmute);
        _rawNoti = Load(noti);
        RefreshBuffers();
    }

    private byte[] Load(Uri uri)
    {
        try 
        {
            using var s = AssetLoader.Open(uri);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        } 
        catch { return Array.Empty<byte>(); }
    }

    private void RefreshBuffers()
    {
        Free();
        _pMute = Scale(_rawMute, _volume);
        _pUnmute = Scale(_rawUnmute, _volume);
        _pNoti = Scale(_rawNoti, _volume);
    }

    private IntPtr Scale(byte[] src, float vol)
    {
        if (src.Length == 0) return IntPtr.Zero;
        byte[] dest = new byte[src.Length];
        Array.Copy(src, dest, src.Length);

        if (vol < 0.99f)
        {
            for (int i = 44; i < dest.Length - 1; i += 2)
            {
                short sample = (short)(BitConverter.ToInt16(dest, i) * vol);
                BitConverter.GetBytes(sample).CopyTo(dest, i);
            }
        }
        
        IntPtr p = Marshal.AllocHGlobal(dest.Length);
        Marshal.Copy(dest, 0, p, dest.Length);
        return p;
    }

    public void PlayMuted() => Play(_pMute);
    public void PlayUnmuted() => Play(_pUnmute);
    public void PlayNoti() => Play(_pNoti);

    private void Play(IntPtr p) { if (p != IntPtr.Zero) PlaySound(p, IntPtr.Zero, FLAGS); }

    private void Free()
    {
        if (_pMute != IntPtr.Zero) Marshal.FreeHGlobal(_pMute);
        if (_pUnmute != IntPtr.Zero) Marshal.FreeHGlobal(_pUnmute);
        if (_pNoti != IntPtr.Zero) Marshal.FreeHGlobal(_pNoti);
        _pMute = _pUnmute = _pNoti = IntPtr.Zero;
    }

    public void Dispose() => Free();
}