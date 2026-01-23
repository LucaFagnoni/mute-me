using System.Text;

namespace Mute_Me;

public class SettingsManager
{
    private static readonly string Path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user.cfg");

    public int SfxVolume { get; set; } = 100;
    public bool RequireShift { get; set; } = false;
    public bool RequireCtrl { get; set; } = false;
    public bool RequireAlt { get; set; } = false;
    public int CurrentHotkey { get; set; } = 124;
    public int IconCorner { get; set; } = 0; // 0=TopLeft, 1=TopRight, 2=BottomLeft, 3=BottomRight
    public int IconMargin { get; set; } = 2;


    public void Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"SfxVolume={SfxVolume}");
            sb.AppendLine($"RequireShift={RequireShift}");
            sb.AppendLine($"RequireCtrl={RequireCtrl}");
            sb.AppendLine($"RequireAlt={RequireAlt}");
            sb.AppendLine($"CurrentHotkey={CurrentHotkey}");
            sb.AppendLine($"IconCorner={IconCorner}");
            sb.AppendLine($"IconMargin={IconMargin}");

            File.WriteAllText(Path, sb.ToString());
        }
        catch { }
    }

    public static SettingsManager Load()
    {
        var s = new SettingsManager();
        if (!File.Exists(Path)) return s;
        try
        {
            foreach (var line in File.ReadAllLines(Path))
            {
                var p = line.Split('=');
                if (p.Length != 2) continue;
                var v = p[1].Trim();
                switch (p[0].Trim())
                {
                    case "SfxVolume": int.TryParse(v, out int vol); s.SfxVolume = vol; break;
                    case "RequireShift": bool.TryParse(v, out bool sh); s.RequireShift = sh; break;
                    case "RequireCtrl": bool.TryParse(v, out bool ct); s.RequireCtrl = ct; break;
                    case "RequireAlt": bool.TryParse(v, out bool al); s.RequireAlt = al; break;
                    case "CurrentHotkey": int.TryParse(v, out int hk); s.CurrentHotkey = hk; break;
                    case "IconCorner": int.TryParse(v, out int ic); s.IconCorner = ic; break;
                    case "IconMargin": int.TryParse(v, out int im); s.IconMargin = im; break;
                }

            }
        }
        catch { }
        return s;
    }
}