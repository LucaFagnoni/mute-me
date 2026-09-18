using System.Runtime.InteropServices;

namespace Mute_Me;

/// Traduce un virtual-key code nel nome del tasto secondo il layout di tastiera
/// attivo, cosi il menu mostra "F13" invece di "124".
public static class KeyNames
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, [Out] char[] lpString, int cchSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint MAPVK_VK_TO_VSC_EX = 4;

    public static string Describe(int vk) => vk switch
    {
        // Windows non sa nominare questi due: PrintScreen non ha voce in tabella
        // e Pause condivide lo scancode (0xE11D) con Right Ctrl, quindi verrebbe
        // fuori il nome sbagliato.
        19 => "Pause",
        44 => "Print Screen",

        // F13..F24 spesso non hanno uno scancode mappabile, ma il nome e' regolare
        // (112 = F1, quindi vk - 111). E' anche il default dell app: vk 124 = F13.
        >= 124 and <= 135 => "F" + (vk - 111),

        _ => FromLayout(vk)
    };

    private static string FromLayout(int vk)
    {
        uint sc = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
        if (sc == 0) return "#" + vk;

        int lParam = (int)(sc & 0xFF) << 16;

        // MapVirtualKey non segnala il prefisso E0 per tutti i tasti estesi:
        // senza IsExtended le frecce diventano tasti del tastierino numerico
        // (freccia destra -> "NUM 6", Insert -> "NUM 0").
        if ((sc & 0xFF00) == 0xE000 || IsExtended(vk)) lParam |= 1 << 24;

        var buf = new char[64];
        int len = GetKeyNameText(lParam, buf, buf.Length);

        // Fallback al codice numerico: meglio "#42" di una stringa vuota nel menu.
        return len > 0 ? new string(buf, 0, len) : "#" + vk;
    }

    private static bool IsExtended(int vk) =>
        vk is 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40   // PgUp PgDn End Home Left Up Right Down
           or 45 or 46 or 91 or 92 or 93 or 144 or 163 or 165; // Ins Del LWin RWin Apps NumLock RCtrl RAlt
}
