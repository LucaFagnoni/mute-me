using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Mute_Me;

public class SoundManager : IDisposable
{
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x00000001;
    private const uint SND_MEMORY = 0x00000004;
    private const uint SND_NODEFAULT = 0x00000002;

    // Original audio data (loaded from Assets)
    private readonly byte[] _originalOnBytes;
    private readonly byte[] _originalOffBytes;
    private readonly byte[] _originalNotiBytes;

    // Unmanaged memory pointers containing audio with volume applied
    private IntPtr _ptrPlayableOn = IntPtr.Zero;
    private IntPtr _ptrPlayableOff = IntPtr.Zero;
    private IntPtr _ptrPlayableNoti = IntPtr.Zero;
    
    private float _currentVolume = 1.0f; // 1.0 = 100%

    /// <summary>
    /// Sets volume from 0 to 100.
    /// Immediately recalculate audio buffer.
    /// </summary>
    public int Volume
    {
        get => (int)(_currentVolume * 100);
        set
        {
            float newVol = Math.Clamp(value, 0, 100) / 100f;
            if (Math.Abs(_currentVolume - newVol) > 0.01f)
            {
                _currentVolume = newVol;
                UpdateVolumeBuffers();
            }
        }
    }

    public SoundManager(Uri uriOn, Uri uriOff, Uri uriNoti)
    {
        _originalOnBytes = LoadAssetBytes(uriOn);
        _originalOffBytes = LoadAssetBytes(uriOff);
        _originalNotiBytes = LoadAssetBytes(uriNoti);

        UpdateVolumeBuffers();
    }

    private byte[] LoadAssetBytes(Uri uri)
    {
        try
        {
            using (var stream = AssetLoader.Open(uri))
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Loading error {uri}: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    private void UpdateVolumeBuffers()
    {
        FreeMemory();
        
        _ptrPlayableOn = CreateVolumeScaledBuffer(_originalOnBytes, _currentVolume);
        _ptrPlayableOff = CreateVolumeScaledBuffer(_originalOffBytes, _currentVolume);
        _ptrPlayableNoti = CreateVolumeScaledBuffer(_originalNotiBytes, _currentVolume);
    }

    private IntPtr CreateVolumeScaledBuffer(byte[] original, float volume)
    {
        if (original.Length == 0) return IntPtr.Zero;
        
        byte[] processed = new byte[original.Length];
        Array.Copy(original, processed, original.Length);

        // PCM SCALING
        // WAV file header is 44 bytes. Audio data follows after.
        // We assume 16-bit PCM (standard). 
        // Every sample is 2 byte (short).
        
        int headerSize = 44; // Standard WAV header size
        
        // If volume is 1.0 (100%), we do no calculations
        if (volume < 0.99f)
        {
            for (int i = headerSize; i < processed.Length - 1; i += 2)
            {
                // Read 16 bit sample
                short sample = BitConverter.ToInt16(processed, i);
                
                // Apply volume
                sample = (short)(sample * volume);
                
                // Write back bytes
                byte[] bytes = BitConverter.GetBytes(sample);
                processed[i] = bytes[0];
                processed[i + 1] = bytes[1];
            }
        }

        // Allocate unmanaged memory for PlaySound
        IntPtr ptr = Marshal.AllocHGlobal(processed.Length);
        Marshal.Copy(processed, 0, ptr, processed.Length);
        return ptr;
    }

    public void PlayMuted() => Play(_ptrPlayableOn);
    public void PlayUnmuted() => Play(_ptrPlayableOff);
    public void PlayNoti() => Play(_ptrPlayableNoti);

    private void Play(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            // SND_MEMORY tells Windows to read from RAM
            PlaySound(ptr, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
    }

    private void FreeMemory()
    {
        if (_ptrPlayableOn != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_ptrPlayableOn);
            _ptrPlayableOn = IntPtr.Zero;
        }
        if (_ptrPlayableOff != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_ptrPlayableOff);
            _ptrPlayableOff = IntPtr.Zero;
        }
        if (_ptrPlayableNoti != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_ptrPlayableNoti);
            _ptrPlayableNoti = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        FreeMemory();
    }
}
