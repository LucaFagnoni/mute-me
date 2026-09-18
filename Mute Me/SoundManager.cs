using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Mute_Me;

public enum Sfx { Mute, Unmute, Noti }

public class SoundManager : IDisposable
{
    [DllImport("winmm.dll")] private static extern bool PlaySound(IntPtr p, IntPtr h, uint f);
    private const uint FLAGS = 0x0004 | 0x0001 | 0x0002; // MEMORY | ASYNC | NODEFAULT

    private readonly byte[][] _raw;
    private readonly IntPtr[] _buf;
    private float _volume = 1.0f;

    public int Volume
    {
        get => (int)(_volume * 100);
        set { _volume = Math.Clamp(value, 0, 100) / 100f; RefreshBuffers(); }
    }

    public SoundManager(Uri mute, Uri unmute, Uri noti)
    {
        _raw = [Load(mute), Load(unmute), Load(noti)];
        _buf = Array.ConvertAll(_raw, b => Marshal.AllocHGlobal(b.Length));
        RefreshBuffers();
    }

    public void Play(Sfx sfx)
    {
        IntPtr p = _buf[(int)sfx];
        if (p != IntPtr.Zero && _raw[(int)sfx].Length > 0) PlaySound(p, IntPtr.Zero, FLAGS);
    }

    public void Dispose()
    {
        for (int i = 0; i < _buf.Length; i++)
        {
            if (_buf[i] != IntPtr.Zero) Marshal.FreeHGlobal(_buf[i]);
            _buf[i] = IntPtr.Zero;
        }
    }

    private static byte[] Load(Uri uri)
    {
        try
        {
            using var s = AssetLoader.Open(uri);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return []; }
    }

    private void RefreshBuffers()
    {
        for (int i = 0; i < _raw.Length; i++) FillBuffer(_raw[i], _buf[i]);
    }

    private void FillBuffer(byte[] src, IntPtr dest)
    {
        if (src.Length == 0 || dest == IntPtr.Zero) return;

        // Volume pieno: copia grezza, niente riscalatura campione per campione
        if (_volume >= 0.99f)
        {
            Marshal.Copy(src, 0, dest, src.Length);
            return;
        }

        byte[] temp = new byte[src.Length];
        Array.Copy(src, temp, src.Length);

        // 44 = header WAV PCM standard, i campioni a 16 bit iniziano dopo
        for (int i = 44; i < temp.Length - 1; i += 2)
        {
            short sample = (short)(BitConverter.ToInt16(temp, i) * _volume);
            BitConverter.GetBytes(sample).CopyTo(temp, i);
        }

        Marshal.Copy(temp, 0, dest, temp.Length);
    }
}
