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
    public int CurrentHotkey { get; set; } = 124;
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
                return JsonSerializer.Deserialize(File.ReadAllText(Path), SettingsJson.Default.SettingsManager) ?? new SettingsManager();
        }
        catch { }
        return new SettingsManager();
    }
}

// Source-gen: obbligatorio sotto Native AOT, il serializer reflection-based non c'e'.
[JsonSerializable(typeof(SettingsManager))]
internal partial class SettingsJson : JsonSerializerContext;
