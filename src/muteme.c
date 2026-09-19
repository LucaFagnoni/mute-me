/*
 * Mute Me - utility da tray per mutare il microfono con una hotkey globale.
 *
 * Win32 puro: nessun runtime gestito, nessun framework UI, nessuna DLL oltre a
 * quelle di sistema. Due finestre in tutto:
 *   - g_ctrl    finestra nascosta: possiede l'icona di tray, riceve il raw input
 *               della tastiera e i comandi del menu. Non e' message-only perche'
 *               TrackPopupMenu ha bisogno di una finestra che possa andare in
 *               foreground per chiudersi correttamente.
 *   - g_overlay finestra a strati, click-through, sempre in cima: disegna l'icona
 *               di stato in un angolo dello schermo.
 *
 * Gli asset (icone gia' in BGRA, wav, xml del task) sono incorporati come RCDATA,
 * quindi non serve nessun decoder di immagini a runtime.
 */

#define WIN32_LEAN_AND_MEAN
#define COBJMACROS

#include <windows.h>
#include <shellapi.h>
#include <mmsystem.h>
#include <mmdeviceapi.h>
#include <endpointvolume.h>
#include <stdio.h>

#include "resource.h"

/* ------------------------------------------------------------------ costanti */

#define APP_NAME        L"Mute Me"
#define TASK_NAME       L"MuteMe_AutoStart"
#define WM_TRAY         (WM_APP + 1)

/* Dimensione logica dell'overlay. 32 e' quanto misura davvero l'icona a schermo
   nella versione Avalonia: il Width="10" del XAML non veniva mai applicato perche'
   Windows impone SM_CXMINTRACK/SM_CYMINTRACK alla finestra. */
#define OVERLAY_LOGICAL 32
#define OVERLAY_SRC     64   /* lato delle blob overlay_*.bgra (premoltiplicate) */
#define TRAY_SRC        32   /* lato delle blob tray_*.bgra (alpha diretto) */

#define ALPHA_MUTED     204  /* 0.8 */
#define ALPHA_UNMUTED   64   /* 0.25 */

#define DEFAULT_SCANCODE 0x64 /* F13 */

static const int VOLUME_STEPS[] = { 0, 20, 40, 60, 80, 100 };
static const int MARGIN_STEPS[] = { 0, 2, 4, 8, 16, 32 };
#define VOLUME_STEP_COUNT ((int)(sizeof(VOLUME_STEPS) / sizeof(VOLUME_STEPS[0])))
#define MARGIN_STEP_COUNT ((int)(sizeof(MARGIN_STEPS) / sizeof(MARGIN_STEPS[0])))

/* GUID definiti a mano: in C non c'e' __uuidof e cosi' evitiamo di dipendere da
   initguid.h / uuid.lib. */
static const GUID CLSID_MMDeviceEnumerator_ =
    { 0xBCDE0395, 0xE52F, 0x467C, { 0x8E, 0x3D, 0xC4, 0x57, 0x92, 0x91, 0x69, 0x2E } };
static const GUID IID_IMMDeviceEnumerator_ =
    { 0xA95664D2, 0x9614, 0x4F35, { 0xA7, 0x46, 0xDE, 0x8D, 0xB6, 0x36, 0x17, 0xE6 } };
static const GUID IID_IAudioEndpointVolume_ =
    { 0x5CDF2C82, 0x841E, 0x4546, { 0x97, 0x22, 0x0C, 0xF7, 0x40, 0x78, 0x22, 0x9A } };

/* -------------------------------------------------------------------- stato */

typedef struct {
    int sfxVolume;      /* 0..100 */
    int requireShift, requireCtrl, requireAlt;
    int hotkeyScanCode; /* scancode hardware del tasto fisico */
    int hotkeyExtended; /* prefisso E0 */
    int iconCorner;     /* 0=TL 1=TR 2=BL 3=BR */
    int iconMargin;     /* px */
} Settings;

typedef struct {
    const BYTE *raw;    /* puntatore dentro la risorsa, sola lettura */
    DWORD       size;
    BYTE       *scaled; /* copia riscalata di volume, NULL se volume = 100% */
} Sfx;

static Settings g_cfg = { 100, 0, 0, 0, DEFAULT_SCANCODE, 0, 0, 2 };

static HINSTANCE g_inst;
static HWND      g_ctrl, g_overlay;
static HMENU     g_menu;
static HICON     g_iconMuted, g_iconUnmuted;
static HWINEVENTHOOK g_winEventHook;
static NOTIFYICONDATAW g_nid;

static const BYTE *g_ovMuted, *g_ovUnmuted;
static Sfx  g_sfx[3];            /* 0 = mute, 1 = unmute, 2 = notifica */
static BOOL g_isMuted;
static BOOL g_recording;
static BOOL g_autoStartOn;

static IMMDeviceEnumerator  *g_enumerator;
static IAudioEndpointVolume *g_volume;

/* ---------------------------------------------------------------- impostazioni */

static void ConfigPath(wchar_t *out, size_t count)
{
    wchar_t *slash;
    GetModuleFileNameW(NULL, out, (DWORD)count);
    slash = wcsrchr(out, L'\\');
    if (slash) wcscpy_s(slash + 1, count - (size_t)(slash + 1 - out), L"user.cfg");
}

static void SettingsLoad(void)
{
    wchar_t path[MAX_PATH];
    char line[128], key[64];
    int value;
    FILE *f = NULL;

    ConfigPath(path, MAX_PATH);
    if (_wfopen_s(&f, path, L"r") != 0 || !f) return;

    while (fgets(line, sizeof(line), f)) {
        if (sscanf_s(line, "%63[^=]=%d", key, (unsigned)sizeof(key), &value) != 2) continue;

        if      (!strcmp(key, "SfxVolume"))      g_cfg.sfxVolume      = value;
        else if (!strcmp(key, "RequireShift"))   g_cfg.requireShift   = value;
        else if (!strcmp(key, "RequireCtrl"))    g_cfg.requireCtrl    = value;
        else if (!strcmp(key, "RequireAlt"))     g_cfg.requireAlt     = value;
        else if (!strcmp(key, "HotkeyScanCode")) g_cfg.hotkeyScanCode = value;
        else if (!strcmp(key, "HotkeyExtended")) g_cfg.hotkeyExtended = value;
        else if (!strcmp(key, "IconCorner"))     g_cfg.iconCorner     = value;
        else if (!strcmp(key, "IconMargin"))     g_cfg.iconMargin     = value;
    }
    fclose(f);

    if (g_cfg.sfxVolume < 0 || g_cfg.sfxVolume > 100) g_cfg.sfxVolume = 100;
    if (g_cfg.iconCorner < 0 || g_cfg.iconCorner > 3) g_cfg.iconCorner = 0;
    if (g_cfg.iconMargin < 0 || g_cfg.iconMargin > 999) g_cfg.iconMargin = 2;
    if (g_cfg.hotkeyScanCode <= 0 || g_cfg.hotkeyScanCode > 0xFF) g_cfg.hotkeyScanCode = DEFAULT_SCANCODE;
}

static void SettingsSave(void)
{
    wchar_t path[MAX_PATH];
    FILE *f = NULL;

    ConfigPath(path, MAX_PATH);
    if (_wfopen_s(&f, path, L"w") != 0 || !f) return;

    fprintf(f, "SfxVolume=%d\n",      g_cfg.sfxVolume);
    fprintf(f, "RequireShift=%d\n",   g_cfg.requireShift);
    fprintf(f, "RequireCtrl=%d\n",    g_cfg.requireCtrl);
    fprintf(f, "RequireAlt=%d\n",     g_cfg.requireAlt);
    fprintf(f, "HotkeyScanCode=%d\n", g_cfg.hotkeyScanCode);
    fprintf(f, "HotkeyExtended=%d\n", g_cfg.hotkeyExtended);
    fprintf(f, "IconCorner=%d\n",     g_cfg.iconCorner);
    fprintf(f, "IconMargin=%d\n",     g_cfg.iconMargin);
    fclose(f);
}

/* ---------------------------------------------------------------------- audio */

static void AudioReset(void)
{
    if (g_volume)     { IAudioEndpointVolume_Release(g_volume);     g_volume = NULL; }
    if (g_enumerator) { IMMDeviceEnumerator_Release(g_enumerator); g_enumerator = NULL; }
}

static IAudioEndpointVolume *AudioEndpoint(void)
{
    IMMDevice *device = NULL;

    if (g_volume) return g_volume;

    if (FAILED(CoCreateInstance(&CLSID_MMDeviceEnumerator_, NULL, CLSCTX_ALL,
                                &IID_IMMDeviceEnumerator_, (void **)&g_enumerator))) {
        g_enumerator = NULL;
        return NULL;
    }
    if (FAILED(IMMDeviceEnumerator_GetDefaultAudioEndpoint(g_enumerator, eCapture, eConsole, &device))) {
        AudioReset();
        return NULL;
    }
    if (FAILED(IMMDevice_Activate(device, &IID_IAudioEndpointVolume_, CLSCTX_ALL, NULL, (void **)&g_volume)))
        g_volume = NULL;

    IMMDevice_Release(device);
    if (!g_volume) AudioReset();
    return g_volume;
}

/* Due tentativi: il primo fallisce se l'endpoint in cache e' morto (microfono
   scollegato, cambio di device predefinito), il secondo gira su uno appena creato. */
static BOOL AudioSetMute(BOOL mute)
{
    int attempt;
    for (attempt = 0; attempt < 2; attempt++) {
        IAudioEndpointVolume *v = AudioEndpoint();
        if (!v) return FALSE;
        if (SUCCEEDED(IAudioEndpointVolume_SetMute(v, mute, NULL))) {
            g_isMuted = mute;
            return TRUE;
        }
        AudioReset();
    }
    return FALSE;
}

static BOOL AudioGetMute(BOOL *out)
{
    int attempt;
    for (attempt = 0; attempt < 2; attempt++) {
        BOOL muted;
        IAudioEndpointVolume *v = AudioEndpoint();
        if (!v) return FALSE;
        if (SUCCEEDED(IAudioEndpointVolume_GetMute(v, &muted))) {
            g_isMuted = muted;
            if (out) *out = muted;
            return TRUE;
        }
        AudioReset();
    }
    return FALSE;
}

/* --------------------------------------------------------------------- suoni */

static const BYTE *LoadAsset(int id, DWORD *size)
{
    HRSRC   res = FindResourceW(g_inst, MAKEINTRESOURCEW(id), RT_RCDATA);
    HGLOBAL mem;
    if (!res) return NULL;
    mem = LoadResource(g_inst, res);
    if (!mem) return NULL;
    if (size) *size = SizeofResource(g_inst, res);
    return (const BYTE *)LockResource(mem);
}

/* Riscala i campioni PCM a 16 bit. A volume pieno non copiamo niente e
   suoniamo direttamente dalla risorsa. */
static void SfxApplyVolume(void)
{
    int i;
    for (i = 0; i < 3; i++) {
        Sfx *s = &g_sfx[i];

        if (s->scaled) { free(s->scaled); s->scaled = NULL; }
        if (!s->raw || s->size <= 44 || g_cfg.sfxVolume >= 100) continue;

        s->scaled = (BYTE *)malloc(s->size);
        if (!s->scaled) continue;
        memcpy(s->scaled, s->raw, s->size);

        /* 44 = header WAV PCM standard, i campioni iniziano dopo. */
        {
            DWORD off;
            for (off = 44; off + 1 < s->size; off += 2) {
                short sample;
                memcpy(&sample, s->scaled + off, sizeof(sample));
                sample = (short)(sample * g_cfg.sfxVolume / 100);
                memcpy(s->scaled + off, &sample, sizeof(sample));
            }
        }
    }
}

static void SfxInit(void)
{
    g_sfx[0].raw = LoadAsset(IDR_WAV_MUTE,   &g_sfx[0].size);
    g_sfx[1].raw = LoadAsset(IDR_WAV_UNMUTE, &g_sfx[1].size);
    g_sfx[2].raw = LoadAsset(IDR_WAV_NOTI,   &g_sfx[2].size);
    SfxApplyVolume();
}

static void SfxPlay(int index)
{
    const Sfx *s = &g_sfx[index];
    const BYTE *data = s->scaled ? s->scaled : s->raw;
    if (!data || g_cfg.sfxVolume == 0) return;
    PlaySoundW((LPCWSTR)data, NULL, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
}

/* ---------------------------------------------------------------- nome tasto */

/* Il nome arriva dal layout di tastiera attivo, non da una tabella qui dentro:
   cosi' segue la tastiera che l'utente ha davvero. */
static void KeyName(int scanCode, BOOL extended, wchar_t *out, int count)
{
    LONG lParam = (LONG)((scanCode & 0xFF) << 16);
    int len, i;
    BOOL usable;

    if (extended) lParam |= 1 << 24;

    len = GetKeyNameTextW(lParam, out, count);
    usable = (len > 0);
    for (i = 0; i < len; i++)
        if (out[i] < 0x20) usable = FALSE;  /* scancode fuori layout: caratteri di controllo */

    if (usable) return;

    /* F13..F24 hanno scancode standard ma quasi nessun layout li nomina. */
    if (!extended && scanCode >= 0x64 && scanCode <= 0x6F)
        _snwprintf_s(out, count, _TRUNCATE, L"F%d", scanCode - 0x57);
    else if (extended)
        _snwprintf_s(out, count, _TRUNCATE, L"SC E0%02X", scanCode);
    else
        _snwprintf_s(out, count, _TRUNCATE, L"SC %02X", scanCode);
}

/* ------------------------------------------------------------------- overlay */

static HICON IconFromBgra(const BYTE *bgra, int side)
{
    BITMAPINFO bi;
    HDC     screen;
    HBITMAP color, mask;
    ICONINFO ii;
    HICON   icon;
    void   *bits = NULL;

    if (!bgra) return NULL;

    ZeroMemory(&bi, sizeof(bi));
    bi.bmiHeader.biSize        = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth       = side;
    bi.bmiHeader.biHeight      = -side;      /* top-down */
    bi.bmiHeader.biPlanes      = 1;
    bi.bmiHeader.biBitCount    = 32;
    bi.bmiHeader.biCompression = BI_RGB;

    screen = GetDC(NULL);
    color  = CreateDIBSection(screen, &bi, DIB_RGB_COLORS, &bits, NULL, 0);
    ReleaseDC(NULL, screen);
    if (!color || !bits) { if (color) DeleteObject(color); return NULL; }

    memcpy(bits, bgra, (size_t)side * side * 4);

    mask = CreateBitmap(side, side, 1, 1, NULL);
    ZeroMemory(&ii, sizeof(ii));
    ii.fIcon    = TRUE;
    ii.hbmMask  = mask;
    ii.hbmColor = color;
    icon = CreateIconIndirect(&ii);

    DeleteObject(color);
    DeleteObject(mask);
    return icon;
}

static POINT OverlayPosition(int side)
{
    RECT work;
    POINT p;
    int m = g_cfg.iconMargin;

    if (!SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0)) {
        work.left = work.top = 0;
        work.right  = GetSystemMetrics(SM_CXSCREEN);
        work.bottom = GetSystemMetrics(SM_CYSCREEN);
    }

    switch (g_cfg.iconCorner) {
    case 1:  p.x = work.right - side - m; p.y = work.top + m;           break;
    case 2:  p.x = work.left + m;         p.y = work.bottom - side - m; break;
    case 3:  p.x = work.right - side - m; p.y = work.bottom - side - m; break;
    default: p.x = work.left + m;         p.y = work.top + m;           break;
    }
    return p;
}

static void OverlayRedraw(void)
{
    const BYTE *src = g_isMuted ? g_ovMuted : g_ovUnmuted;
    BITMAPINFO biSrc, biDst;
    HDC screen, dcSrc, dcDst;
    HBITMAP bmSrc, bmDst;
    HGDIOBJ oldSrc, oldDst;
    void *srcBits = NULL, *dstBits = NULL;
    BLENDFUNCTION blend;
    POINT ptSrc = { 0, 0 }, ptDst;
    SIZE size;
    UINT dpi;
    int side;

    if (!g_overlay || !src) return;

    dpi = GetDpiForWindow(g_overlay);
    if (dpi == 0) dpi = 96;
    side = MulDiv(OVERLAY_LOGICAL, (int)dpi, 96);
    if (side < 4) side = 4;

    ZeroMemory(&biSrc, sizeof(biSrc));
    biSrc.bmiHeader.biSize        = sizeof(BITMAPINFOHEADER);
    biSrc.bmiHeader.biWidth       = OVERLAY_SRC;
    biSrc.bmiHeader.biHeight      = -OVERLAY_SRC;
    biSrc.bmiHeader.biPlanes      = 1;
    biSrc.bmiHeader.biBitCount    = 32;
    biSrc.bmiHeader.biCompression = BI_RGB;

    biDst = biSrc;
    biDst.bmiHeader.biWidth  = side;
    biDst.bmiHeader.biHeight = -side;

    screen = GetDC(NULL);
    dcSrc  = CreateCompatibleDC(screen);
    dcDst  = CreateCompatibleDC(screen);
    bmSrc  = CreateDIBSection(screen, &biSrc, DIB_RGB_COLORS, &srcBits, NULL, 0);
    bmDst  = CreateDIBSection(screen, &biDst, DIB_RGB_COLORS, &dstBits, NULL, 0);

    if (bmSrc && bmDst && srcBits && dstBits) {
        memcpy(srcBits, src, (size_t)OVERLAY_SRC * OVERLAY_SRC * 4);

        oldSrc = SelectObject(dcSrc, bmSrc);
        oldDst = SelectObject(dcDst, bmDst);

        /* I dati sono premoltiplicati, quindi interpolare tutti e quattro i
           canali e' corretto e non produce aloni sui bordi. */
        SetStretchBltMode(dcDst, HALFTONE);
        SetBrushOrgEx(dcDst, 0, 0, NULL);
        StretchBlt(dcDst, 0, 0, side, side, dcSrc, 0, 0, OVERLAY_SRC, OVERLAY_SRC, SRCCOPY);

        ptDst = OverlayPosition(side);
        size.cx = side;
        size.cy = side;

        blend.BlendOp             = AC_SRC_OVER;
        blend.BlendFlags          = 0;
        blend.SourceConstantAlpha = (BYTE)(g_isMuted ? ALPHA_MUTED : ALPHA_UNMUTED);
        blend.AlphaFormat         = AC_SRC_ALPHA;

        UpdateLayeredWindow(g_overlay, screen, &ptDst, &size, dcDst, &ptSrc, 0, &blend, ULW_ALPHA);

        SelectObject(dcSrc, oldSrc);
        SelectObject(dcDst, oldDst);
    }

    if (bmSrc) DeleteObject(bmSrc);
    if (bmDst) DeleteObject(bmDst);
    DeleteDC(dcSrc);
    DeleteDC(dcDst);
    ReleaseDC(NULL, screen);
}

static void OverlayEnforceTopmost(void)
{
    if (g_overlay)
        SetWindowPos(g_overlay, HWND_TOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

/* Windows ci sveglia quando un'altra finestra prende il foreground: e' l'unico
   momento in cui possiamo finire coperti. Niente timer di polling.
   ponytail: solo eventi di foreground. Un'app che va topmost senza prenderlo
   (certi overlay di gioco) ci copre ancora; in quel caso aggiungere
   EVENT_OBJECT_SHOW allo stesso hook. */
static void CALLBACK ForegroundChanged(HWINEVENTHOOK hook, DWORD event, HWND hwnd,
                                       LONG idObject, LONG idChild, DWORD thread, DWORD time)
{
    (void)hook; (void)event; (void)hwnd; (void)idObject; (void)idChild; (void)thread; (void)time;
    OverlayEnforceTopmost();
}

/* -------------------------------------------------------------- tray e menu */

static void TrayUpdateIcon(void)
{
    g_nid.uFlags = NIF_ICON;
    g_nid.hIcon  = g_isMuted ? g_iconMuted : g_iconUnmuted;
    Shell_NotifyIconW(NIM_MODIFY, &g_nid);
}

static void TrayBalloon(const wchar_t *text)
{
    g_nid.uFlags = NIF_INFO;
    g_nid.dwInfoFlags = NIIF_NONE;
    wcscpy_s(g_nid.szInfoTitle, ARRAYSIZE(g_nid.szInfoTitle), APP_NAME);
    wcscpy_s(g_nid.szInfo, ARRAYSIZE(g_nid.szInfo), text);
    Shell_NotifyIconW(NIM_MODIFY, &g_nid);
}

static void MenuRefresh(void)
{
    wchar_t label[128], key[64];
    int i, modifier;

    CheckMenuItem(g_menu, IDM_MUTE, MF_BYCOMMAND | (g_isMuted ? MF_CHECKED : MF_UNCHECKED));
    CheckMenuItem(g_menu, IDM_AUTOSTART, MF_BYCOMMAND | (g_autoStartOn ? MF_CHECKED : MF_UNCHECKED));

    for (i = 0; i < VOLUME_STEP_COUNT; i++)
        CheckMenuItem(g_menu, IDM_VOLUME_BASE + i,
                      MF_BYCOMMAND | (g_cfg.sfxVolume == VOLUME_STEPS[i] ? MF_CHECKED : MF_UNCHECKED));

    modifier = g_cfg.requireShift ? 1 : g_cfg.requireCtrl ? 2 : g_cfg.requireAlt ? 3 : 0;
    CheckMenuRadioItem(g_menu, IDM_MODIFIER_BASE, IDM_MODIFIER_BASE + 3,
                       IDM_MODIFIER_BASE + modifier, MF_BYCOMMAND);

    CheckMenuRadioItem(g_menu, IDM_CORNER_BASE, IDM_CORNER_BASE + 3,
                       IDM_CORNER_BASE + g_cfg.iconCorner, MF_BYCOMMAND);

    for (i = 0; i < MARGIN_STEP_COUNT; i++)
        CheckMenuItem(g_menu, IDM_MARGIN_BASE + i,
                      MF_BYCOMMAND | (g_cfg.iconMargin == MARGIN_STEPS[i] ? MF_CHECKED : MF_UNCHECKED));

    KeyName(g_cfg.hotkeyScanCode, g_cfg.hotkeyExtended, key, ARRAYSIZE(key));
    _snwprintf_s(label, ARRAYSIZE(label), _TRUNCATE, L"Set Hotkey (Current: %s)", key);
    ModifyMenuW(g_menu, IDM_HOTKEY, MF_BYCOMMAND | MF_STRING, IDM_HOTKEY, label);
}

static void MenuBuild(void)
{
    HMENU volume = CreatePopupMenu();
    HMENU modifiers = CreatePopupMenu();
    HMENU position = CreatePopupMenu();
    HMENU margin = CreatePopupMenu();
    wchar_t label[64];
    int i;

    static const wchar_t *MODIFIER_NAMES[] = { L"None", L"Shift", L"Ctrl", L"Alt" };
    static const wchar_t *CORNER_NAMES[]   = { L"Top Left", L"Top Right", L"Bottom Left", L"Bottom Right" };

    for (i = 0; i < VOLUME_STEP_COUNT; i++) {
        _snwprintf_s(label, ARRAYSIZE(label), _TRUNCATE, L"%d%%", VOLUME_STEPS[i]);
        AppendMenuW(volume, MF_STRING, IDM_VOLUME_BASE + i, label);
    }
    for (i = 0; i < 4; i++)
        AppendMenuW(modifiers, MF_STRING, IDM_MODIFIER_BASE + i, MODIFIER_NAMES[i]);
    for (i = 0; i < MARGIN_STEP_COUNT; i++) {
        _snwprintf_s(label, ARRAYSIZE(label), _TRUNCATE, L"%d px", MARGIN_STEPS[i]);
        AppendMenuW(margin, MF_STRING, IDM_MARGIN_BASE + i, label);
    }
    for (i = 0; i < 4; i++)
        AppendMenuW(position, MF_STRING, IDM_CORNER_BASE + i, CORNER_NAMES[i]);
    AppendMenuW(position, MF_SEPARATOR, 0, NULL);
    AppendMenuW(position, MF_POPUP, (UINT_PTR)margin, L"Margin");

    g_menu = CreatePopupMenu();
    AppendMenuW(g_menu, MF_STRING, IDM_MUTE, L"Mute");
    AppendMenuW(g_menu, MF_POPUP, (UINT_PTR)volume, L"Sfx Volume");
    AppendMenuW(g_menu, MF_POPUP, (UINT_PTR)modifiers, L"Modifiers");
    AppendMenuW(g_menu, MF_POPUP, (UINT_PTR)position, L"Icon Position");
    AppendMenuW(g_menu, MF_STRING, IDM_HOTKEY, L"Set Hotkey");
    AppendMenuW(g_menu, MF_STRING, IDM_AUTOSTART, L"Auto Start (Admin)");
    AppendMenuW(g_menu, MF_SEPARATOR, 0, NULL);
    AppendMenuW(g_menu, MF_STRING, IDM_EXIT, L"Exit");
}

static void MenuShow(void)
{
    POINT cursor;
    GetCursorPos(&cursor);
    MenuRefresh();

    /* Senza SetForegroundWindow il menu resta aperto quando si clicca altrove. */
    SetForegroundWindow(g_ctrl);
    TrackPopupMenu(g_menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, cursor.x, cursor.y, 0, g_ctrl, NULL);
    PostMessageW(g_ctrl, WM_NULL, 0, 0);
}

/* ---------------------------------------------------------------- auto start */

static int RunSchTasks(const wchar_t *args)
{
    wchar_t cmd[1024];
    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    DWORD code = (DWORD)-1;

    _snwprintf_s(cmd, ARRAYSIZE(cmd), _TRUNCATE, L"schtasks.exe %s", args);

    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;
    ZeroMemory(&pi, sizeof(pi));

    if (!CreateProcessW(NULL, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi))
        return -1;

    WaitForSingleObject(pi.hProcess, 10000);
    GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return (int)code;
}

static BOOL AutoStartActive(void)
{
    return RunSchTasks(L"/Query /TN \"" TASK_NAME L"\" /FO CSV") == 0;
}

static void AutoStartInstall(void)
{
    const BYTE *xml;
    DWORD xmlSize = 0;
    wchar_t tempDir[MAX_PATH], tempFile[MAX_PATH], exePath[MAX_PATH], args[1024];
    wchar_t *buffer, *placeholder;
    size_t chars;
    HANDLE file;
    DWORD written;

    xml = LoadAsset(IDR_TASK_XML, &xmlSize);
    if (!xml || xmlSize < 2) return;

    /* La risorsa e' gia' UTF-16LE con BOM, come la vuole schtasks /XML. */
    chars = xmlSize / sizeof(wchar_t);
    buffer = (wchar_t *)malloc((chars + MAX_PATH + 1) * sizeof(wchar_t));
    if (!buffer) return;
    memcpy(buffer, xml, xmlSize);
    buffer[chars] = 0;

    GetModuleFileNameW(NULL, exePath, MAX_PATH);

    placeholder = wcsstr(buffer, L"{EXE_PATH}");
    if (placeholder) {
        wchar_t tail[1024];
        wcscpy_s(tail, ARRAYSIZE(tail), placeholder + wcslen(L"{EXE_PATH}"));
        *placeholder = 0;
        wcscat_s(buffer, chars + MAX_PATH + 1, exePath);
        wcscat_s(buffer, chars + MAX_PATH + 1, tail);
    }

    GetTempPathW(MAX_PATH, tempDir);
    GetTempFileNameW(tempDir, L"mm", 0, tempFile);

    file = CreateFileW(tempFile, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, NULL);
    if (file != INVALID_HANDLE_VALUE) {
        WriteFile(file, buffer, (DWORD)(wcslen(buffer) * sizeof(wchar_t)), &written, NULL);
        CloseHandle(file);

        _snwprintf_s(args, ARRAYSIZE(args), _TRUNCATE,
                     L"/Create /TN \"" TASK_NAME L"\" /XML \"%s\" /F", tempFile);
        RunSchTasks(args);
    }

    DeleteFileW(tempFile);
    free(buffer);
}

static void AutoStartToggle(void)
{
    if (AutoStartActive())
        RunSchTasks(L"/Delete /TN \"" TASK_NAME L"\" /F");
    else
        AutoStartInstall();

    g_autoStartOn = AutoStartActive();
}

/* ----------------------------------------------------------------- microfono */

static void SetMicrophone(BOOL mute)
{
    AudioSetMute(mute);
    SfxPlay(g_isMuted ? 0 : 1);
    OverlayRedraw();
    TrayUpdateIcon();
}

/* ----------------------------------------------------------------- raw input */

static BOOL IsModifierScanCode(int scanCode)
{
    /* Posizioni fisiche, identiche su ogni layout: Ctrl, LShift, RShift, Alt,
       tasti Windows e Menu (questi ultimi arrivano con prefisso E0). */
    return scanCode == 0x1D || scanCode == 0x2A || scanCode == 0x36 ||
           scanCode == 0x38 || scanCode == 0x5B || scanCode == 0x5C || scanCode == 0x5D;
}

static void HandleKey(int scanCode, BOOL extended)
{
    BOOL shift, ctrl, alt;

    if (g_recording) {
        if (IsModifierScanCode(scanCode)) return;

        g_cfg.hotkeyScanCode = scanCode;
        g_cfg.hotkeyExtended = extended;
        g_recording = FALSE;
        SettingsSave();
        SfxPlay(2);
        return;
    }

    if (scanCode != g_cfg.hotkeyScanCode || (extended ? 1 : 0) != g_cfg.hotkeyExtended) return;

    shift = (GetAsyncKeyState(VK_SHIFT)   & 0x8000) != 0;
    ctrl  = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
    alt   = (GetAsyncKeyState(VK_MENU)    & 0x8000) != 0;

    if (shift == (g_cfg.requireShift != 0) &&
        ctrl  == (g_cfg.requireCtrl  != 0) &&
        alt   == (g_cfg.requireAlt   != 0))
        SetMicrophone(!g_isMuted);
}

static void ProcessRawInput(LPARAM lParam)
{
    UINT size = 0;
    BYTE stack[256];
    BYTE *buffer = stack;
    RAWINPUT *raw;
    int scanCode;
    BOOL extended;

    GetRawInputData((HRAWINPUT)lParam, RID_INPUT, NULL, &size, sizeof(RAWINPUTHEADER));
    if (size == 0) return;
    if (size > sizeof(stack)) {
        buffer = (BYTE *)malloc(size);
        if (!buffer) return;
    }

    if (GetRawInputData((HRAWINPUT)lParam, RID_INPUT, buffer, &size, sizeof(RAWINPUTHEADER)) == size) {
        raw = (RAWINPUT *)buffer;
        if (raw->header.dwType == RIM_TYPEKEYBOARD) {
            const RAWKEYBOARD *kb = &raw->data.keyboard;

            /* Solo pressioni, e mai il riempitivo 0xFF che certe tastiere mandano
               dentro le sequenze escape. */
            if (!(kb->Flags & RI_KEY_BREAK) && kb->VKey != 0xFF) {
                scanCode = kb->MakeCode;
                extended = (kb->Flags & RI_KEY_E0) != 0;

                /* Pause arriva come E1 1D: Windows lo nomina sullo scancode 0x45
                   non esteso (0x45 esteso e' Num Lock). */
                if (kb->Flags & RI_KEY_E1) {
                    scanCode = 0x45;
                    extended = FALSE;
                }

                /* Tastiere virtuali, KVM e sessioni remote mandano a volte solo il
                   virtual key: ricostruiamo lo scancode dal layout come ripiego. */
                if (scanCode == 0) {
                    UINT mapped = MapVirtualKeyW(kb->VKey, MAPVK_VK_TO_VSC_EX);
                    if (mapped == 0) goto done;
                    scanCode = (int)(mapped & 0xFF);
                    extended = (mapped & 0xFF00) == 0xE000;
                }

                HandleKey(scanCode, extended);
            }
        }
    }

done:
    if (buffer != stack) free(buffer);
}

/* ------------------------------------------------------------ window proc */

static LRESULT CALLBACK CtrlProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg) {
    case WM_INPUT:
        ProcessRawInput(lParam);
        return DefWindowProcW(hwnd, msg, wParam, lParam);

    case WM_TRAY:
        if (LOWORD(lParam) == WM_RBUTTONUP || LOWORD(lParam) == WM_CONTEXTMENU)
            MenuShow();
        else if (LOWORD(lParam) == WM_LBUTTONDBLCLK)
            SetMicrophone(!g_isMuted);
        return 0;

    case WM_COMMAND: {
        int id = LOWORD(wParam);

        if (id == IDM_MUTE) {
            SetMicrophone(!g_isMuted);
        } else if (id >= IDM_VOLUME_BASE && id < IDM_VOLUME_BASE + VOLUME_STEP_COUNT) {
            g_cfg.sfxVolume = VOLUME_STEPS[id - IDM_VOLUME_BASE];
            SfxApplyVolume();
            SettingsSave();
            SfxPlay(2);
        } else if (id >= IDM_MODIFIER_BASE && id < IDM_MODIFIER_BASE + 4) {
            int which = id - IDM_MODIFIER_BASE;
            g_cfg.requireShift = (which == 1);
            g_cfg.requireCtrl  = (which == 2);
            g_cfg.requireAlt   = (which == 3);
            SettingsSave();
        } else if (id >= IDM_CORNER_BASE && id < IDM_CORNER_BASE + 4) {
            g_cfg.iconCorner = id - IDM_CORNER_BASE;
            SettingsSave();
            OverlayRedraw();
        } else if (id >= IDM_MARGIN_BASE && id < IDM_MARGIN_BASE + MARGIN_STEP_COUNT) {
            g_cfg.iconMargin = MARGIN_STEPS[id - IDM_MARGIN_BASE];
            SettingsSave();
            OverlayRedraw();
        } else if (id == IDM_HOTKEY) {
            g_recording = TRUE;
            SfxPlay(2);
            TrayBalloon(L"Premi il tasto da assegnare...");
        } else if (id == IDM_AUTOSTART) {
            AutoStartToggle();
        } else if (id == IDM_EXIT) {
            DestroyWindow(hwnd);
        }
        return 0;
    }

    case WM_DPICHANGED:
    case WM_DISPLAYCHANGE:
    case WM_SETTINGCHANGE:
        OverlayRedraw();
        return 0;

    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

static LRESULT CALLBACK OverlayProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

/* ----------------------------------------------------------------- avvio */

static BOOL CreateWindows(void)
{
    WNDCLASSEXW wc;
    RAWINPUTDEVICE rid;

    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = CtrlProc;
    wc.hInstance     = g_inst;
    wc.lpszClassName = L"MuteMeControl";
    if (!RegisterClassExW(&wc)) return FALSE;

    /* Finestra normale mai mostrata, non message-only: TrackPopupMenu richiede
       una finestra che possa andare in foreground. WS_EX_TOOLWINDOW la tiene
       fuori da taskbar e alt-tab. */
    g_ctrl = CreateWindowExW(WS_EX_TOOLWINDOW, wc.lpszClassName, APP_NAME,
                             WS_POPUP, 0, 0, 0, 0, NULL, NULL, g_inst, NULL);
    if (!g_ctrl) return FALSE;

    wc.lpfnWndProc   = OverlayProc;
    wc.lpszClassName = L"MuteMeOverlay";
    if (!RegisterClassExW(&wc)) return FALSE;

    g_overlay = CreateWindowExW(
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
        wc.lpszClassName, APP_NAME, WS_POPUP, 0, 0, 16, 16, NULL, NULL, g_inst, NULL);
    if (!g_overlay) return FALSE;
    ShowWindow(g_overlay, SW_SHOWNOACTIVATE);

    rid.usUsagePage = 0x01;  /* Generic Desktop */
    rid.usUsage     = 0x06;  /* Keyboard */
    rid.dwFlags     = RIDEV_INPUTSINK;
    rid.hwndTarget  = g_ctrl;
    RegisterRawInputDevices(&rid, 1, sizeof(rid));

    return TRUE;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE prev, PWSTR cmdLine, int show)
{
    MSG msg;
    HANDLE single;

    (void)prev; (void)cmdLine; (void)show;
    g_inst = instance;

    /* Una sola istanza: la seconda esce senza toccare niente. */
    single = CreateMutexW(NULL, TRUE, L"Local\\MuteMe_SingleInstance");
    if (!single || GetLastError() == ERROR_ALREADY_EXISTS) return 0;

    if (FAILED(CoInitializeEx(NULL, COINIT_APARTMENTTHREADED))) return 1;

    SettingsLoad();

    g_ovMuted   = LoadAsset(IDR_OVERLAY_MUTED, NULL);
    g_ovUnmuted = LoadAsset(IDR_OVERLAY_UNMUTED, NULL);
    g_iconMuted   = IconFromBgra(LoadAsset(IDR_TRAY_MUTED, NULL), TRAY_SRC);
    g_iconUnmuted = IconFromBgra(LoadAsset(IDR_TRAY_UNMUTED, NULL), TRAY_SRC);
    SfxInit();

    if (!CreateWindows()) return 1;
    MenuBuild();

    ZeroMemory(&g_nid, sizeof(g_nid));
    g_nid.cbSize           = sizeof(g_nid);
    g_nid.hWnd             = g_ctrl;
    g_nid.uID              = 1;
    g_nid.uFlags           = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    g_nid.uCallbackMessage = WM_TRAY;
    g_nid.hIcon            = g_iconUnmuted;
    wcscpy_s(g_nid.szTip, ARRAYSIZE(g_nid.szTip), APP_NAME);
    Shell_NotifyIconW(NIM_ADD, &g_nid);

    AudioGetMute(NULL);
    g_autoStartOn = AutoStartActive();
    OverlayRedraw();
    TrayUpdateIcon();

    g_winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                                     NULL, ForegroundChanged, 0, 0,
                                     WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    /* Uscita: smuta il microfono come faceva la versione precedente. */
    if (g_winEventHook) UnhookWinEvent(g_winEventHook);
    SettingsSave();
    AudioSetMute(FALSE);
    Shell_NotifyIconW(NIM_DELETE, &g_nid);
    AudioReset();
    CoUninitialize();
    return 0;
}
