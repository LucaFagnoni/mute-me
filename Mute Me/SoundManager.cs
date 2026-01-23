using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Mute_Me;

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
        
        // Allocate buffers once
        _pMute = Marshal.AllocHGlobal(_rawMute.Length);
        _pUnmute = Marshal.AllocHGlobal(_rawUnmute.Length);
        _pNoti = Marshal.AllocHGlobal(_rawNoti.Length);
        
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
        FillBuffer(_rawMute, _pMute, _volume);
        FillBuffer(_rawUnmute, _pUnmute, _volume);
        FillBuffer(_rawNoti, _pNoti, _volume);
    }

    private void FillBuffer(byte[] src, IntPtr dest, float vol)
    {
        if (src.Length == 0 || dest == IntPtr.Zero) return;
        
        // Opt: If volume is 100%, just copy raw
        if (vol >= 0.99f)
        {
            Marshal.Copy(src, 0, dest, src.Length);
            return;
        }

        byte[] temp = new byte[src.Length];
        Array.Copy(src, temp, src.Length);

        for (int i = 44; i < temp.Length - 1; i += 2)
        {
            short sample = (short)(BitConverter.ToInt16(temp, i) * vol);
            BitConverter.GetBytes(sample).CopyTo(temp, i);
        }
        
        Marshal.Copy(temp, 0, dest, temp.Length);
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