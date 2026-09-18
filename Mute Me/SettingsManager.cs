using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mute_Me;

public class SettingsManager
{
    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "user.cfg");

    public int SfxVolume { get; set; } = 100;
    public bool RequireShift { get; set; }
    public bool RequireCtrl { get; set; }
    public bool RequireAlt { get; set; }
    // Scancode del tasto fisico. 0x64 = F13, il default storico.
    // 0 significa "non impostato": Load() lo risolve.
    public int HotkeyScanCode { get; set; }
    public bool HotkeyExtended { get; set; }

    // ponytail: virtual key, formato fino alla v1.2.0r2. Tenuto solo per convertire
    // le config gia' scritte su disco. Eliminabile quando nessuno aggiorna piu' da li'.
    public int CurrentHotkey { get; set; }
    public int IconCorner { get; set; } // 0=TopLeft, 1=TopRight, 2=BottomLeft, 3=BottomRight
    public int IconMargin { get; set; } = 2;

    public void Save()
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(this, SettingsJson.Default.SettingsManager)); }
        catch { }
    }

    public static SettingsManager Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(Path), SettingsJson.Default.SettingsManager);
                if (loaded != null)
                {
                    loaded.ResolveHotkey();
                    return loaded;
                }
            }
        }
        catch { }

        var fresh = new SettingsManager();
        fresh.ResolveHotkey();
        return fresh;
    }

    private const int DEFAULT_SCANCODE = 0x64; // F13

    /// Converte l'hotkey dal vecchio formato a virtual key, una volta sola.
    /// ponytail: MapVirtualKey non segnala il prefisso E0 per frecce e blocco
    /// navigazione, quindi chi aveva mappato una freccia se la ritrova sul
    /// tastierino e deve riassegnarla. Migrazione una-tantum, non vale di piu'.
    private void ResolveHotkey()
    {
        if (HotkeyScanCode != 0) return;

        if (CurrentHotkey != 0)
        {
            uint mapped = MapVirtualKey((uint)CurrentHotkey, MAPVK_VK_TO_VSC_EX);
            if (mapped != 0)
            {
                HotkeyScanCode = (int)(mapped & 0xFF);
                HotkeyExtended = (mapped & 0xFF00) == 0xE000;
                return;
            }
        }

        HotkeyScanCode = DEFAULT_SCANCODE;
        HotkeyExtended = false;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    private const uint MAPVK_VK_TO_VSC_EX = 4;
}

// Source-gen: obbligatorio sotto Native AOT, il serializer reflection-based non c'e'.
[JsonSerializable(typeof(SettingsManager))]
internal partial class SettingsJson : JsonSerializerContext;
