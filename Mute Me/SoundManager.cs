using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform; // Necessario per AssetLoader

namespace Mute_Me
{
    public class SoundManager : IDisposable
    {
        // --- P/INVOKE WINMM.DLL ---
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

        // Costanti aggiornate per riproduzione da MEMORIA
        private const uint SND_ASYNC  = 0x00000001; // Non bloccare l'app
        private const uint SND_MEMORY = 0x00000004; // Il primo parametro è un puntatore in memoria
        private const uint SND_NODEFAULT = 0x00000002; // Niente bip di sistema se fallisce

        // Manteniamo i puntatori alla memoria non gestita
        private IntPtr _ptrMuteOn = IntPtr.Zero;
        private IntPtr _ptrMuteOff = IntPtr.Zero;

        /// <summary>
        /// Carica i suoni dagli Assets di Avalonia.
        /// Esempio path: "avares://NomeTuoProgetto/Assets/mute_on.wav"
        /// </summary>
        public SoundManager(Uri uriMuteOn, Uri uriMuteOff)
        {
            _ptrMuteOn = LoadAssetToMemory(uriMuteOn);
            _ptrMuteOff = LoadAssetToMemory(uriMuteOff);
        }

        private IntPtr LoadAssetToMemory(Uri assetUri)
        {
            try
            {
                // 1. Apri lo stream con AssetLoader
                using (Stream stream = AssetLoader.Open(assetUri))
                {
                    // 2. Leggi tutto lo stream in un array di byte gestito
                    byte[] data = new byte[stream.Length];
                    stream.ReadExactly(data);

                    // 3. Alloca memoria non gestita (HGlobal)
                    IntPtr pUnmanagedBytes = Marshal.AllocHGlobal(data.Length);

                    // 4. Copia i byte dall'array gestito alla memoria non gestita
                    Marshal.Copy(data, 0, pUnmanagedBytes, data.Length);

                    return pUnmanagedBytes;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SoundManager] Errore caricamento asset {assetUri}: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        public void PlayMuted()
        {
            Play(_ptrMuteOn);
        }

        public void PlayUnmuted()
        {
            Play(_ptrMuteOff);
        }

        private void Play(IntPtr ptrSound)
        {
            if (ptrSound != IntPtr.Zero)
            {
                // Passiamo il puntatore alla memoria e il flag SND_MEMORY
                PlaySound(ptrSound, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
            }
        }

        // Pulizia della memoria quando chiudi l'app
        public void Dispose()
        {
            if (_ptrMuteOn != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_ptrMuteOn);
                _ptrMuteOn = IntPtr.Zero;
            }

            if (_ptrMuteOff != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_ptrMuteOff);
                _ptrMuteOff = IntPtr.Zero;
            }
        }
    }
}