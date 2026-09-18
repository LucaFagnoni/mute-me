using System.Runtime.InteropServices;

namespace Mute_Me;

/// Nome del tasto a partire dallo scancode hardware.
///
/// Lo scancode identifica il tasto FISICO: arriva dal raw input prima di ogni
/// traduzione di layout, quindi non cambia se l utente commuta da IT a RU a JP.
/// GetKeyNameText lo risolve attraverso il layout attivo, percio il nome esce
/// nella lingua e nella convenzione della tastiera che l utente ha davvero,
/// senza tabelle hardcoded qui dentro.
public static class KeyNames
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, [Out] char[] lpString, int cchSize);

    public static string Describe(int scanCode, bool extended)
    {
        var buf = new char[64];
        int len = GetKeyNameText(LParam(scanCode, extended), buf, buf.Length);

        if (len > 0)
        {
            var name = new string(buf, 0, len).Trim();
            // Alcuni scancode fuori dal layout attivo tornano caratteri di controllo
            // (0x7C su layout US torna un TAB letterale): non sono nomi.
            if (name.Length > 0 && !name.Any(char.IsControl)) return name;
        }

        // F13..F24 hanno scancode standard ma quasi nessun layout li nomina.
        if (!extended && scanCode is >= 0x64 and <= 0x6F) return "F" + (scanCode - 0x57);

        // Tasto sconosciuto a questo layout: mostra lo scancode grezzo, cosi resta
        // comunque identificabile e riassegnabile.
        return extended ? $"SC E0{scanCode:X2}" : $"SC {scanCode:X2}";
    }

    private static int LParam(int scanCode, bool extended)
    {
        int lParam = (scanCode & 0xFF) << 16;
        if (extended) lParam |= 1 << 24;
        return lParam;
    }
}
