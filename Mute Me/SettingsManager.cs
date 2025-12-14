using System.Text;

namespace Mute_Me;

public class SettingsManager
{
    public int SfxVolume { get; set; } = 100;
    public bool RequireShift { get; set; } = false;
    public bool RequireCtrl { get; set; } = false;
    public bool RequireAlt { get; set; } = false;
    public int CurrentHotkey { get; set; } = 124; // Default F13
    
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user.cfg");
    
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

            File.WriteAllText(ConfigPath, sb.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager] Saving error: {ex.Message}");
        }
    }

    public static SettingsManager Load()
    {
        var settings = new SettingsManager();

        if (!File.Exists(ConfigPath))
        {
            settings.Save();
            return settings;
        }

        try
        {
            string[] lines = File.ReadAllLines(ConfigPath);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                var parts = line.Split('=');
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                string value = parts[1].Trim();

                switch (key)
                {
                    case "SfxVolume":
                        if (int.TryParse(value, out int vol)) settings.SfxVolume = Math.Clamp(vol, 0, 100);
                        break;
                    case "RequireShift":
                        if (bool.TryParse(value, out bool shift)) settings.RequireShift = shift;
                        break;
                    case "RequireCtrl":
                        if (bool.TryParse(value, out bool ctrl)) settings.RequireCtrl = ctrl;
                        break;
                    case "RequireAlt":
                        if (bool.TryParse(value, out bool alt)) settings.RequireAlt = alt;
                        break;
                    case "CurrentHotkey":
                        if (int.TryParse(value, out int hotkey)) settings.CurrentHotkey = hotkey;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager] Loading Error: {ex.Message}");
        }

        return settings;
    }
}
