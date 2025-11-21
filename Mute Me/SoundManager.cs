using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Mute_Me;

public class SoundManager : IDisposable
{
    // Importiamo PlaySound per riprodurre da memoria
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x00000001;
    private const uint SND_MEMORY = 0x00000004;
    private const uint SND_NODEFAULT = 0x00000002;

    // Dati audio originali (caricati dagli Assets)
    private readonly byte[] _originalOnBytes;
    private readonly byte[] _originalOffBytes;

    // Puntatori alla memoria non gestita che contengono l'audio col volume applicato
    private IntPtr _ptrPlayableOn = IntPtr.Zero;
    private IntPtr _ptrPlayableOff = IntPtr.Zero;

    // Dimensioni dei buffer
    private int _sizeOn;
    private int _sizeOff;

    private float _currentVolume = 1.0f; // 1.0 = 100%

    /// <summary>
    /// Imposta il volume da 0 a 100.
    /// Ricalcola immediatamente i buffer audio.
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
                UpdateVolumeBuffers(); // Ricalcola l'audio
            }
        }
    }

    public SoundManager(Uri uriOn, Uri uriOff)
    {
        // 1. Carica i dati grezzi dagli Assets in array di byte gestiti
        _originalOnBytes = LoadAssetBytes(uriOn);
        _originalOffBytes = LoadAssetBytes(uriOff);

        _sizeOn = _originalOnBytes.Length;
        _sizeOff = _originalOffBytes.Length;

        // 2. Crea i buffer iniziali (Volume 100%)
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
            System.Diagnostics.Debug.WriteLine($"Errore caricamento {uri}: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    private void UpdateVolumeBuffers()
    {
        // Libera la memoria precedente
        FreeMemory();

        // Genera nuovi buffer con il volume applicato
        _ptrPlayableOn = CreateVolumeScaledBuffer(_originalOnBytes, _currentVolume);
        _ptrPlayableOff = CreateVolumeScaledBuffer(_originalOffBytes, _currentVolume);
    }

    private IntPtr CreateVolumeScaledBuffer(byte[] original, float volume)
    {
        if (original.Length == 0) return IntPtr.Zero;

        // Copia l'array originale
        byte[] processed = new byte[original.Length];
        Array.Copy(original, processed, original.Length);

        // --- PCM SCALING (Magia del Volume) ---
        // Un file WAV ha un header di 44 byte. I dati audio iniziano dopo.
        // Assumiamo 16-bit PCM (standard). 
        // Ogni campione sono 2 byte (short).
        
        int headerSize = 44; // Standard WAV header size
        
        // Se il volume è 1.0 (100%), non facciamo calcoli (risparmio CPU)
        if (volume < 0.99f)
        {
            for (int i = headerSize; i < processed.Length - 1; i += 2)
            {
                // Leggi il campione a 16 bit
                short sample = BitConverter.ToInt16(processed, i);
                
                // Applica il volume
                sample = (short)(sample * volume);
                
                // Scrivi indietro i byte
                byte[] bytes = BitConverter.GetBytes(sample);
                processed[i] = bytes[0];
                processed[i + 1] = bytes[1];
            }
        }

        // Alloca memoria non gestita per PlaySound
        IntPtr ptr = Marshal.AllocHGlobal(processed.Length);
        Marshal.Copy(processed, 0, ptr, processed.Length);
        return ptr;
    }

    public void PlayMuted() => Play(_ptrPlayableOn);
    public void PlayUnmuted() => Play(_ptrPlayableOff);

    private void Play(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            // SND_MEMORY dice a Windows di leggere dal puntatore RAM
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
    }

    public void Dispose()
    {
        FreeMemory();
    }
}
