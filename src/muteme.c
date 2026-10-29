/*
 * Mute Me - utility da tray per mutare il microfono con una hotkey globale.
 *
 * Win32 puro: nessun runtime gestito, nessun framework UI, nessuna DLL oltre a
 * quelle di sistema. Due thread:
 *   - UI     g_ctrl    finestra nascosta, possiede l'icona di tray.
 *            g_overlay finestra a strati, click-through, sempre in cima: disegna
 *                      l'icona di stato in un angolo dello schermo.
 *            g_popup   pannello impostazioni, esiste solo mentre e' aperto.
 *   - input  g_input   finestra message-only su un thread TIME_CRITICAL: riceve il
 *                      raw input, muta il microfono e suona. Non tocca mai Explorer
 *                      ne' GDI, cosi' l'hotkey risponde anche con la UI bloccata o
 *                      la CPU satura (un gioco che compila shader).
 *
 * Gli asset (icone gia' in BGRA, wav, xml del task) sono incorporati come RCDATA,
 * quindi non serve nessun decoder di immagini a runtime.
 */

#define WIN32_LEAN_AND_MEAN
#define COBJMACROS

#include <windows.h>
#include <shellapi.h>
#include <mmdeviceapi.h>
#include <endpointvolume.h>
#include <audioclient.h>
#include <dwmapi.h>
#include <math.h>
#include <stdio.h>

#include "resource.h"

/* ------------------------------------------------------------------ costanti */

#define APP_NAME        L"Mute Me"
#define TASK_NAME       L"MuteMe_AutoStart"
#define WM_TRAY         (WM_APP + 1)
#define WM_TOGGLE       (WM_APP + 2) /* -> g_input: inverti il mute */
#define WM_MIC_CHANGED  (WM_APP + 3) /* -> g_ctrl: ridisegna overlay, tray e pannello */
#define WM_HOTKEY_SET   (WM_APP + 4) /* -> g_ctrl: registrazione dell'hotkey finita */

#define WATCHDOG_TIMER  1
#define WATCHDOG_MS     5000 /* ogni quanto ricontrolliamo raw input e icona di tray */
#define SCANCODE_ESC    0x01
#define HOLD_TIMEOUT_MS 1500 /* oltre il ritardo massimo del typematic (1 s) */

#ifndef WDA_EXCLUDEFROMCAPTURE
#define WDA_EXCLUDEFROMCAPTURE 0x00000011  /* SDK precedenti a Windows 10 2004 */
#endif

/* Dimensione logica dell'overlay. 32 e' quanto misura davvero l'icona a schermo
   nella versione Avalonia: il Width="10" del XAML non veniva mai applicato perche'
   Windows impone SM_CXMINTRACK/SM_CYMINTRACK alla finestra. */
#define OVERLAY_LOGICAL 32
#define OVERLAY_SRC     64   /* lato delle blob overlay_*.bgra (premoltiplicate) */
#define TRAY_SRC        32   /* lato delle blob tray_*.bgra (alpha diretto) */

#define ALPHA_MUTED     204  /* 0.8 */
#define ALPHA_UNMUTED   64   /* 0.25 */

#define DEFAULT_SCANCODE 0x64 /* F13 */

static const int MARGIN_STEPS[] = { 0, 2, 4, 8, 16, 32 };
#define MARGIN_STEP_COUNT ((int)(sizeof(MARGIN_STEPS) / sizeof(MARGIN_STEPS[0])))

/* GUID definiti a mano: in C non c'e' __uuidof e cosi' evitiamo di dipendere da
   initguid.h / uuid.lib. */
static const GUID CLSID_MMDeviceEnumerator_ =
    { 0xBCDE0395, 0xE52F, 0x467C, { 0x8E, 0x3D, 0xC4, 0x57, 0x92, 0x91, 0x69, 0x2E } };
static const GUID IID_IMMDeviceEnumerator_ =
    { 0xA95664D2, 0x9614, 0x4F35, { 0xA7, 0x46, 0xDE, 0x8D, 0xB6, 0x36, 0x17, 0xE6 } };
static const GUID IID_IAudioEndpointVolume_ =
    { 0x5CDF2C82, 0x841E, 0x4546, { 0x97, 0x22, 0x0C, 0xF7, 0x40, 0x78, 0x22, 0x9A } };
static const GUID IID_IAudioClient_ =
    { 0x1CB9AD4C, 0xDBFA, 0x4C32, { 0xB1, 0x78, 0xC2, 0xF5, 0x68, 0xA7, 0x03, 0xB2 } };
static const GUID IID_IAudioRenderClient_ =
    { 0xF294ACFC, 0x3146, 0x4483, { 0xA7, 0xBF, 0xAD, 0xDC, 0xA7, 0xC2, 0x60, 0xE2 } };
static const GUID IID_IAudioStreamVolume_ =
    { 0x93014887, 0x242D, 0x4068, { 0x8A, 0x15, 0xCF, 0x5E, 0x93, 0xB9, 0x0F, 0xE3 } };

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
} Sfx;

static Settings g_cfg = { 100, 0, 0, 0, DEFAULT_SCANCODE, 0, 0, 2 };

static HINSTANCE g_inst;
static HWND      g_ctrl, g_overlay, g_input;
static HICON     g_iconMuted, g_iconUnmuted;
static HWINEVENTHOOK g_winEventHook;
static NOTIFYICONDATAW g_nid;

static const BYTE *g_ovMuted, *g_ovUnmuted;
static Sfx  g_sfx[3];            /* 0 = mute, 1 = unmute, 2 = notifica */
/* Scritti dal thread input, letti anche dalla UI: BOOL allineati, basta volatile. */
static volatile BOOL g_isMuted;
static volatile BOOL g_recording;
static BOOL g_autoStartOn;

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

/* Sempre il device predefinito del momento, mai in cache: un endpoint tenuto da
   parte resta valido anche dopo un cambio di device predefinito (cuffie collegate),
   e l'hotkey continuerebbe a mutare il microfono vecchio. Il chiamante rilascia. */
static void *AudioActivate(EDataFlow flow, const GUID *iid)
{
    IMMDeviceEnumerator *enumerator = NULL;
    IMMDevice           *device = NULL;
    void                *out = NULL;

    if (FAILED(CoCreateInstance(&CLSID_MMDeviceEnumerator_, NULL, CLSCTX_ALL,
                                &IID_IMMDeviceEnumerator_, (void **)&enumerator)))
        return NULL;

    if (SUCCEEDED(IMMDeviceEnumerator_GetDefaultAudioEndpoint(enumerator, flow, eConsole, &device))) {
        if (FAILED(IMMDevice_Activate(device, iid, CLSCTX_ALL, NULL, &out)))
            out = NULL;
        IMMDevice_Release(device);
    }
    IMMDeviceEnumerator_Release(enumerator);
    return out;
}

static void AudioSetMute(BOOL mute)
{
    IAudioEndpointVolume *v = AudioActivate(eCapture, &IID_IAudioEndpointVolume_);
    if (!v) return;
    if (SUCCEEDED(IAudioEndpointVolume_SetMute(v, mute, NULL))) g_isMuted = mute;
    IAudioEndpointVolume_Release(v);
}

static void AudioGetMute(void)
{
    BOOL muted;
    IAudioEndpointVolume *v = AudioActivate(eCapture, &IID_IAudioEndpointVolume_);
    if (!v) return;
    if (SUCCEEDED(IAudioEndpointVolume_GetMute(v, &muted))) g_isMuted = muted;
    IAudioEndpointVolume_Release(v);
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

static void SfxInit(void)
{
    g_sfx[0].raw = LoadAsset(IDR_WAV_MUTE,   &g_sfx[0].size);
    g_sfx[1].raw = LoadAsset(IDR_WAV_UNMUTE, &g_sfx[1].size);
    g_sfx[2].raw = LoadAsset(IDR_WAV_NOTI,   &g_sfx[2].size);
}

/* WASAPI e non PlaySound: solo una sessione aperta da noi puo' chiedere di non
   comparire nel mixer di Windows (AUDCLNT_SESSIONFLAGS_DISPLAY_HIDE). Il volume
   e' quello dello stream, non della sessione, cosi' Windows non lo ricorda.
   Un thread per suono, che finisce col suono: niente stato da condividere. */
static DWORD WINAPI SfxThread(void *param)
{
    const Sfx *s = &g_sfx[(INT_PTR)param];
    IAudioClient       *client;
    IAudioRenderClient *render = NULL;
    IAudioStreamVolume *volume = NULL;
    WAVEFORMATEX fmt;
    UINT32 total, done = 0, capacity, padding, n, channels, ch;
    float level = g_cfg.sfxVolume / 100.0f;
    BOOL started = FALSE;
    BYTE *dst;

    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    CoInitializeEx(NULL, COINIT_MULTITHREADED);

    client = (IAudioClient *)AudioActivate(eRender, &IID_IAudioClient_);
    if (!client) goto out;

    /* Header WAV canonico da 44 byte: fmt PCM a offset 20, campioni da 44.
       PCMWAVEFORMAT non ha cbSize, quindi va azzerato a mano. */
    memcpy(&fmt, s->raw + 20, 16);
    fmt.cbSize = 0;
    if (fmt.nBlockAlign == 0) goto out;
    total = (s->size - 44) / fmt.nBlockAlign;

    if (FAILED(IAudioClient_Initialize(client, AUDCLNT_SHAREMODE_SHARED,
                   AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY |
                   AUDCLNT_SESSIONFLAGS_DISPLAY_HIDE,
                   10000000 /* 1 s in unita' da 100 ns: i nostri suoni ci stanno interi */,
                   0, &fmt, NULL)) ||
        FAILED(IAudioClient_GetBufferSize(client, &capacity)) ||
        FAILED(IAudioClient_GetService(client, &IID_IAudioRenderClient_, (void **)&render)))
        goto out;

    if (SUCCEEDED(IAudioClient_GetService(client, &IID_IAudioStreamVolume_, (void **)&volume))) {
        if (SUCCEEDED(IAudioStreamVolume_GetChannelCount(volume, &channels)))
            for (ch = 0; ch < channels; ch++) IAudioStreamVolume_SetChannelVolume(volume, ch, level);
        IAudioStreamVolume_Release(volume);
    }

    /* Riempie finche' c'e' da scrivere, poi aspetta che il buffer si svuoti. */
    while (SUCCEEDED(IAudioClient_GetCurrentPadding(client, &padding))) {
        if (done == total) {
            if (padding == 0) break;
        } else {
            n = min(capacity - padding, total - done);
            if (n && SUCCEEDED(IAudioRenderClient_GetBuffer(render, n, &dst))) {
                memcpy(dst, s->raw + 44 + (size_t)done * fmt.nBlockAlign, (size_t)n * fmt.nBlockAlign);
                IAudioRenderClient_ReleaseBuffer(render, n, 0);
                done += n;
            }
        }
        if (!started) {
            if (FAILED(IAudioClient_Start(client))) break;
            started = TRUE;
        }
        Sleep(10);
    }
    if (started) IAudioClient_Stop(client);

out:
    if (render) IAudioRenderClient_Release(render);
    if (client) IAudioClient_Release(client);
    CoUninitialize();
    return 0;
}

static void SfxPlay(int index)
{
    HANDLE thread;
    if (!g_sfx[index].raw || g_sfx[index].size <= 44 || g_cfg.sfxVolume == 0) return;
    thread = CreateThread(NULL, 0, SfxThread, (void *)(INT_PTR)index, 0, NULL);
    if (thread) CloseHandle(thread);
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

static POINT OverlayPosition(int side, int m)
{
    RECT work;
    POINT p;

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

/* Copia riscalata di una blob overlay: ogni pixel e' la media dei pixel sorgente
   che copre. I dati sono premoltiplicati, quindi mediare tutti e quattro i
   canali e' corretto e non produce aloni. Non StretchBlt in HALFTONE: azzera
   l'alpha, e AlphaBlend salta i pixel con alpha 0. Il chiamante cancella. */
static HBITMAP OverlayScaled(const BYTE *src, int side)
{
    BITMAPINFO bi;
    HBITMAP bm;
    BYTE *dst = NULL;
    int x, y, sx, sy, c;

    ZeroMemory(&bi, sizeof(bi));
    bi.bmiHeader.biSize        = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth       = side;
    bi.bmiHeader.biHeight      = -side;
    bi.bmiHeader.biPlanes      = 1;
    bi.bmiHeader.biBitCount    = 32;
    bi.bmiHeader.biCompression = BI_RGB;

    bm = CreateDIBSection(NULL, &bi, DIB_RGB_COLORS, (void **)&dst, NULL, 0);
    if (!bm || !dst) { if (bm) DeleteObject(bm); return NULL; }

    for (y = 0; y < side; y++) {
        int y0 = y * OVERLAY_SRC / side, y1 = max(y0 + 1, (y + 1) * OVERLAY_SRC / side);
        for (x = 0; x < side; x++) {
            int x0 = x * OVERLAY_SRC / side, x1 = max(x0 + 1, (x + 1) * OVERLAY_SRC / side);
            int sum[4] = { 0, 0, 0, 0 }, n = (x1 - x0) * (y1 - y0);

            for (sy = y0; sy < y1; sy++)
                for (sx = x0; sx < x1; sx++)
                    for (c = 0; c < 4; c++) sum[c] += src[(sy * OVERLAY_SRC + sx) * 4 + c];
            for (c = 0; c < 4; c++) dst[(y * side + x) * 4 + c] = (BYTE)(sum[c] / n);
        }
    }
    return bm;
}

static void OverlayRedraw(void)
{
    const BYTE *src = g_isMuted ? g_ovMuted : g_ovUnmuted;
    HDC screen, dc;
    HBITMAP bm;
    HGDIOBJ old;
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

    bm = OverlayScaled(src, side);
    if (!bm) return;

    screen = GetDC(NULL);
    dc     = CreateCompatibleDC(screen);
    old    = SelectObject(dc, bm);

    ptDst = OverlayPosition(side, MulDiv(g_cfg.iconMargin, (int)dpi, 96));
    size.cx = side;
    size.cy = side;

    blend.BlendOp             = AC_SRC_OVER;
    blend.BlendFlags          = 0;
    blend.SourceConstantAlpha = (BYTE)(g_isMuted ? ALPHA_MUTED : ALPHA_UNMUTED);
    blend.AlphaFormat         = AC_SRC_ALPHA;

    UpdateLayeredWindow(g_overlay, screen, &ptDst, &size, dc, &ptSrc, 0, &blend, ULW_ALPHA);

    SelectObject(dc, old);
    DeleteObject(bm);
    DeleteDC(dc);
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

/* NIM_MODIFY fallisce se l'icona non c'e': Explorer riavviato, oppure shell non
   ancora pronta quando il task ci lancia al logon. In quel caso la rimettiamo. */
static void TrayUpdateIcon(void)
{
    g_nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    g_nid.hIcon  = g_isMuted ? g_iconMuted : g_iconUnmuted;
    if (!Shell_NotifyIconW(NIM_MODIFY, &g_nid))
        Shell_NotifyIconW(NIM_ADD, &g_nid);
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

/* Solo sul thread input. Overlay e tray li ridisegna il thread UI quando gli
   arriva il messaggio: se Explorer o la UI sono bloccati, mute e suono partono
   comunque. */
static void ToggleMicrophone(void)
{
    AudioSetMute(!g_isMuted);
    SfxPlay(g_isMuted ? 0 : 1);
    PostMessageW(g_ctrl, WM_MIC_CHANGED, 0, 0);
}

/* ------------------------------------------------------------------ pannello */

/* Pannello impostazioni stile iOS: gruppi arrotondati, switch, controlli
   segmentati, uno slider. Disegnato a mano in un DIB a 32 bit: le forme sono
   tutte rettangoli arrotondati, antialiasati con una distance function, e il
   testo e' GDI normale con ClearType, possibile perche' lo sfondo e' opaco.
   La finestra esiste solo mentre e' aperta: ricrearla a ogni apertura la mette
   al DPI del monitor giusto, e a riposo non costa niente.
   ponytail: niente animazioni (lo switch scatta), aggiungerle con un timer
   sul pannello se servono. */

#define POPUP_W 300   /* larghezza logica a 96 dpi */

enum { HIT_NONE, HIT_MUTE, HIT_KEY, HIT_MODIFIER, HIT_VOLUME, HIT_CORNER, HIT_MARGIN, HIT_AUTOSTART, HIT_QUIT };

typedef struct { RECT rc; int kind, index; } Hit;

/* 0xAARRGGBB, lo stesso ordine dei pixel nel DIB. Colori di sistema iOS. */
typedef struct {
    DWORD bg, card, text, secondary, separator, track, thumb, hover, blue, green, red;
} Palette;

static const Palette PALETTE_LIGHT = {
    0xFFF2F2F7, 0xFFFFFFFF, 0xFF000000, 0xFF8A8A8E, 0xFFE0E0E5, 0xFFE3E3E8,
    0xFFFFFFFF, 0xFFEBEBF0, 0xFF007AFF, 0xFF34C759, 0xFFFF3B30 };
static const Palette PALETTE_DARK = {
    0xFF1C1C1E, 0xFF2C2C2E, 0xFFFFFFFF, 0xFF98989F, 0xFF3D3D41, 0xFF3A3A3C,
    0xFF636366, 0xFF3A3A3C, 0xFF0A84FF, 0xFF30D158, 0xFFFF453A };

static HWND   g_popup;
static UINT   g_popupDpi;
static HFONT  g_fontBody, g_fontTitle, g_fontSmall;
static const Palette *g_pal;
static Hit    g_hits[24];
static int    g_hitCount;
static int    g_hot;        /* HIT_* della riga sotto il mouse */
static BOOL   g_dragging;   /* slider del volume preso col mouse */
static RECT   g_sliderRc;

/* bits NULL = solo layout: serve a misurare l'altezza prima di mostrare. */
static struct { DWORD *bits; int w, h; HDC dc; } g_cv;

static int Px(int v) { return MulDiv(v, (int)g_popupDpi, 96); }

/* Rettangolo arrotondato: ogni pixel prende copertura dalla distanza del suo
   centro dal bordo. stroke = 0 riempie, altrimenti traccia il bordo interno. */
static void Shape(int x, int y, int w, int h, float r, float stroke, DWORD color)
{
    float hw = w / 2.0f, hh = h / 2.0f, cx = x + hw, cy = y + hh;
    float alpha = (color >> 24) / 255.0f;
    int px, py, x0 = max(x, 0), y0 = max(y, 0), x1 = min(x + w, g_cv.w), y1 = min(y + h, g_cv.h);

    if (!g_cv.bits) return;
    GdiFlush();   /* il testo GDI gia' accodato deve finire nel DIB prima di noi */

    for (py = y0; py < y1; py++)
        for (px = x0; px < x1; px++) {
            float qx = fabsf(px + 0.5f - cx) - (hw - r);
            float qy = fabsf(py + 0.5f - cy) - (hh - r);
            float ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
            float d = sqrtf(ox * ox + oy * oy) + min(max(qx, qy), 0) - r;
            float a;
            DWORD *p = &g_cv.bits[py * g_cv.w + px], dst = *p;

            if (stroke > 0) d = fabsf(d + stroke / 2) - stroke / 2;
            a = 0.5f - d;
            if (a <= 0) continue;
            a = (a > 1 ? 1 : a) * alpha;

#define MIX(sh) ((DWORD)((((color >> sh) & 0xFF) * a + ((dst >> sh) & 0xFF) * (1 - a)) + 0.5f) << sh)
            *p = MIX(16) | MIX(8) | MIX(0);
#undef MIX
        }
}

static void Text(int x, int y, int w, int h, const wchar_t *s, HFONT font, DWORD color, UINT align)
{
    RECT rc;
    if (!g_cv.bits) return;
    SetRect(&rc, x, y, x + w, y + h);
    SelectObject(g_cv.dc, font);
    SetTextColor(g_cv.dc, RGB((color >> 16) & 0xFF, (color >> 8) & 0xFF, color & 0xFF));
    DrawTextW(g_cv.dc, s, -1, &rc, align | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
}

static void AddHit(int x, int y, int w, int h, int kind, int index)
{
    Hit *t;
    if (g_hitCount >= (int)ARRAYSIZE(g_hits)) return;
    t = &g_hits[g_hitCount++];
    SetRect(&t->rc, x, y, x + w, y + h);
    t->kind  = kind;
    t->index = index;
}

static void Card(int x, int y, int w, int h) { Shape(x, y, w, h, (float)Px(10), 0, g_pal->card); }

static void Separator(int x, int y, int w) { Shape(x, y, w, max(1, Px(1) / 2), 0, 0, g_pal->separator); }

/* Evidenziazione al passaggio del mouse, rientrata come nei menu di macOS. */
static void Hover(int kind, int x, int y, int w, int h)
{
    if (g_hot == kind) Shape(x + Px(4), y + Px(4), w - Px(8), h - Px(8), (float)Px(7), 0, g_pal->hover);
}

static int Section(int x, int y, int w, const wchar_t *title)
{
    Text(x + Px(16), y + Px(10), w, Px(18), title, g_fontSmall, g_pal->secondary, DT_LEFT);
    return y + Px(30);
}

/* Switch iOS, 51x31. */
static void Toggle(int x, int y, BOOL on, DWORD color)
{
    int w = Px(51), h = Px(31), k = h - Px(4);
    int kx = on ? x + w - k - Px(2) : x + Px(2);

    Shape(x, y, w, h, h / 2.0f, 0, on ? color : g_pal->track);
    Shape(kx, y + Px(3), k, k, k / 2.0f, 0, 0x26000000);
    Shape(kx, y + Px(2), k, k, k / 2.0f, 0, 0xFFFFFFFF);
}

/* Schermo in miniatura con un punto nell'angolo scelto. */
static void CornerGlyph(int cx, int cy, int corner)
{
    int w = Px(18), h = Px(13), d = Px(4), in = Px(3);
    int x = cx - w / 2, y = cy - h / 2;

    Shape(x, y, w, h, (float)Px(3), Px(3) / 2.0f, g_pal->text);
    Shape(corner & 1 ? x + w - in - d : x + in, corner & 2 ? y + h - in - d : y + in,
          d, d, d / 2.0f, 0, g_pal->text);
}

/* Controllo segmentato. labels NULL disegna gli angoli dello schermo. */
static void Segmented(int x, int y, int w, int h, int count, int selected, int kind,
                      const wchar_t *const *labels)
{
    int i, sw = w / count, in = Px(2);
    DWORD divider = (g_pal->secondary & 0x00FFFFFF) | 0x50000000;

    Shape(x, y, sw * count, h, (float)Px(8), 0, g_pal->track);
    for (i = 0; i < count; i++) {
        int sx = x + i * sw;

        if (i == selected) {
            Shape(sx + in, y + in + Px(1), sw - 2 * in, h - 2 * in, (float)Px(7), 0, 0x1A000000);
            Shape(sx + in, y + in, sw - 2 * in, h - 2 * in, (float)Px(7), 0, g_pal->thumb);
        } else if (i > 0 && i - 1 != selected) {
            Shape(sx, y + Px(8), max(1, Px(1)), h - Px(16), 0, 0, divider);
        }

        if (labels) Text(sx, y, sw, h, labels[i], g_fontSmall, g_pal->text, DT_CENTER);
        else        CornerGlyph(sx + sw / 2, y + h / 2, i);
        AddHit(sx, y, sw, h, kind, i);
    }
}

#define SLIDER_KNOB 24

static void Slider(int x, int y, int w, int h, int value)
{
    int t = Px(4), k = Px(SLIDER_KNOB), cy = y + h / 2, range = w - k;
    int kx = x + range * value / 100;

    Shape(x + k / 2, cy - t / 2, range, t, t / 2.0f, 0, g_pal->track);
    Shape(x + k / 2, cy - t / 2, kx - x, t, t / 2.0f, 0, g_pal->blue);
    Shape(kx, cy - k / 2 + Px(1), k, k, k / 2.0f, 0, 0x30000000);
    Shape(kx, cy - k / 2, k, k, k / 2.0f, 0, 0xFFFFFFFF);
    AddHit(x, y, w, h, HIT_VOLUME, 0);
}

/* Icona dell'app: il glifo bianco dell'overlay su un quadrato colorato. */
static void MicTile(int x, int y, int s)
{
    BLENDFUNCTION blend = { AC_SRC_OVER, 0, 255, AC_SRC_ALPHA };
    int g = s * 2 / 3;
    HBITMAP bm;
    HDC dc;
    HGDIOBJ old;

    Shape(x, y, s, s, (float)Px(8), 0, g_isMuted ? g_pal->red : g_pal->green);
    if (!g_cv.bits || !g_ovMuted || !g_ovUnmuted) return;

    bm = OverlayScaled(g_isMuted ? g_ovMuted : g_ovUnmuted, g);
    if (!bm) return;
    dc  = CreateCompatibleDC(g_cv.dc);
    old = SelectObject(dc, bm);
    GdiAlphaBlend(g_cv.dc, x + (s - g) / 2, y + (s - g) / 2, g, g, dc, 0, 0, g, g, blend);
    SelectObject(dc, old);
    DeleteDC(dc);
    DeleteObject(bm);
}

/* Disegna (o, senza bits, misura soltanto) il pannello. Ritorna l'altezza. */
static int PopupLayout(void)
{
    static const wchar_t *MODIFIERS[] = { L"None", L"Shift", L"Ctrl", L"Alt" };
    static const wchar_t *MARGINS[]   = { L"0", L"2", L"4", L"8", L"16", L"32" };
    const Palette *p = g_pal;
    int m = Px(12), x = m, w = g_cv.w - 2 * m, y = m;
    int row = Px(44), pad = Px(16), segW = Px(172), segH = Px(28), segX = x + w - Px(8) - segW;
    int head = Px(64), toggleX = x + w - Px(14 + 51);
    int modifier = g_cfg.requireShift ? 1 : g_cfg.requireCtrl ? 2 : g_cfg.requireAlt ? 3 : 0;
    int margin = -1, i;
    wchar_t key[64], text[128];

    for (i = 0; i < MARGIN_STEP_COUNT; i++)
        if (g_cfg.iconMargin == MARGIN_STEPS[i]) margin = i;

    KeyName(g_cfg.hotkeyScanCode, g_cfg.hotkeyExtended, key, ARRAYSIZE(key));
    g_hitCount = 0;
    Shape(0, 0, g_cv.w, g_cv.h, 0, 0, p->bg);

    /* microfono */
    Card(x, y, w, head);
    MicTile(x + Px(14), y + Px(14), Px(36));
    Text(x + Px(62), y + Px(12), w - Px(140), Px(22), L"Microphone", g_fontTitle, p->text, DT_LEFT);
    _snwprintf_s(text, ARRAYSIZE(text), _TRUNCATE, L"%s  \u00B7  %s%s", g_isMuted ? L"Muted" : L"Live",
                 modifier ? MODIFIERS[modifier] : L"", modifier ? L" + " : L"");
    wcscat_s(text, ARRAYSIZE(text), key);
    Text(x + Px(62), y + Px(33), w - Px(140), Px(18), text, g_fontSmall, g_isMuted ? p->red : p->secondary, DT_LEFT);
    Toggle(toggleX, y + (head - Px(31)) / 2, g_isMuted, p->red);
    AddHit(x, y, w, head, HIT_MUTE, 0);
    y += head;

    /* hotkey */
    y = Section(x, y, w, L"HOTKEY");
    Card(x, y, w, 2 * row);
    Hover(HIT_KEY, x, y, w, row);
    Text(x + pad, y, w, row, L"Key", g_fontBody, p->text, DT_LEFT);
    if (g_recording) {
        Text(x, y, w - pad, row, L"Press a key\u2026", g_fontBody, p->blue, DT_RIGHT);
    } else {
        _snwprintf_s(text, ARRAYSIZE(text), _TRUNCATE, L"%s   \u203A", key);
        Text(x, y, w - pad, row, text, g_fontBody, p->secondary, DT_RIGHT);
    }
    AddHit(x, y, w, row, HIT_KEY, 0);
    Separator(x + pad, y + row, w - pad);
    Text(x + pad, y + row, w, row, L"Modifier", g_fontBody, p->text, DT_LEFT);
    Segmented(segX, y + row + (row - segH) / 2, segW, segH, 4, modifier, HIT_MODIFIER, MODIFIERS);
    y += 2 * row;

    /* suoni */
    y = Section(x, y, w, L"SOUND");
    Card(x, y, w, row);
    Text(x + pad, y, w, row, L"Volume", g_fontBody, p->text, DT_LEFT);
    _snwprintf_s(text, ARRAYSIZE(text), _TRUNCATE, L"%d%%", g_cfg.sfxVolume);
    Text(x, y, w - pad, row, text, g_fontBody, p->secondary, DT_RIGHT);
    Slider(x + Px(80), y, w - Px(80 + 58), row, g_cfg.sfxVolume);
    y += row;

    /* overlay */
    y = Section(x, y, w, L"OVERLAY");
    Card(x, y, w, 2 * row);
    Text(x + pad, y, w, row, L"Corner", g_fontBody, p->text, DT_LEFT);
    Segmented(segX, y + (row - segH) / 2, segW, segH, 4, g_cfg.iconCorner, HIT_CORNER, NULL);
    Separator(x + pad, y + row, w - pad);
    Text(x + pad, y + row, w, row, L"Margin", g_fontBody, p->text, DT_LEFT);
    Segmented(segX, y + row + (row - segH) / 2, segW, segH, MARGIN_STEP_COUNT, margin, HIT_MARGIN, MARGINS);
    y += 2 * row + m;

    /* avvio e uscita */
    Card(x, y, w, row);
    Text(x + pad, y, w, row, L"Start at login", g_fontBody, p->text, DT_LEFT);
    Toggle(toggleX, y + (row - Px(31)) / 2, g_autoStartOn, p->green);
    AddHit(x, y, w, row, HIT_AUTOSTART, 0);
    y += row + m;

    Card(x, y, w, row);
    Hover(HIT_QUIT, x, y, w, row);
    Text(x, y, w, row, L"Quit Mute Me", g_fontBody, p->red, DT_CENTER);
    AddHit(x, y, w, row, HIT_QUIT, 0);
    return y + row + m;
}

static void PopupPaint(HWND hwnd)
{
    PAINTSTRUCT ps;
    BITMAPINFO bi;
    RECT rc;
    HDC dc = BeginPaint(hwnd, &ps);
    HBITMAP bm;
    HGDIOBJ old;
    void *bits = NULL;

    GetClientRect(hwnd, &rc);
    ZeroMemory(&bi, sizeof(bi));
    bi.bmiHeader.biSize        = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth       = rc.right;
    bi.bmiHeader.biHeight      = -rc.bottom;
    bi.bmiHeader.biPlanes      = 1;
    bi.bmiHeader.biBitCount    = 32;
    bi.bmiHeader.biCompression = BI_RGB;

    g_cv.dc = CreateCompatibleDC(dc);
    bm = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, &bits, NULL, 0);
    if (bm && bits) {
        old = SelectObject(g_cv.dc, bm);
        SetBkMode(g_cv.dc, TRANSPARENT);
        g_cv.bits = (DWORD *)bits;
        g_cv.w = rc.right;
        g_cv.h = rc.bottom;
        PopupLayout();
        GdiFlush();
        BitBlt(dc, 0, 0, rc.right, rc.bottom, g_cv.dc, 0, 0, SRCCOPY);
        SelectObject(g_cv.dc, old);
    }
    g_cv.bits = NULL;
    if (bm) DeleteObject(bm);
    DeleteDC(g_cv.dc);
    EndPaint(hwnd, &ps);
}

static const Hit *PopupHit(LPARAM lParam)
{
    POINT pt;
    int i;
    pt.x = (short)LOWORD(lParam);
    pt.y = (short)HIWORD(lParam);
    for (i = 0; i < g_hitCount; i++)
        if (PtInRect(&g_hits[i].rc, pt)) return &g_hits[i];
    return NULL;
}

static void PopupSlide(int mx)
{
    int k = Px(SLIDER_KNOB), range = g_sliderRc.right - g_sliderRc.left - k;
    int v = range > 0 ? (mx - g_sliderRc.left - k / 2) * 100 / range : 0;

    v = (max(0, min(100, v)) + 2) / 5 * 5;
    if (v == g_cfg.sfxVolume) return;
    g_cfg.sfxVolume = v;
    InvalidateRect(g_popup, NULL, FALSE);
}

static void PopupClick(const Hit *h, int mx)
{
    switch (h->kind) {
    case HIT_MUTE:
        PostMessageW(g_input, WM_TOGGLE, 0, 0);  /* ridisegno con WM_MIC_CHANGED */
        return;
    case HIT_KEY:
        g_recording = !g_recording;
        if (g_recording) SfxPlay(2);
        break;
    case HIT_MODIFIER:
        g_cfg.requireShift = (h->index == 1);
        g_cfg.requireCtrl  = (h->index == 2);
        g_cfg.requireAlt   = (h->index == 3);
        SettingsSave();
        break;
    case HIT_VOLUME:
        g_sliderRc = h->rc;
        g_dragging = TRUE;
        SetCapture(g_popup);
        PopupSlide(mx);
        return;
    case HIT_CORNER:
        g_cfg.iconCorner = h->index;
        SettingsSave();
        OverlayRedraw();
        break;
    case HIT_MARGIN:
        g_cfg.iconMargin = MARGIN_STEPS[h->index];
        SettingsSave();
        OverlayRedraw();
        break;
    case HIT_AUTOSTART:
        AutoStartToggle();
        break;
    case HIT_QUIT:
        DestroyWindow(g_ctrl);
        return;
    }
    InvalidateRect(g_popup, NULL, FALSE);
}

static HFONT PopupFont(int px, int weight)
{
    return CreateFontW(-Px(px), 0, 0, 0, weight, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                       OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                       DEFAULT_PITCH, L"Segoe UI");
}

/* Un secondo clic sull'icona di tray chiude il pannello invece di riaprirlo. */
static void PopupToggle(void)
{
    DWORD light = 1, bytes = sizeof(light);
    DWM_WINDOW_CORNER_PREFERENCE corner = DWMWCP_ROUND;
    BOOL dark;
    POINT cursor;
    SIZE size;
    RECT pos;

    if (g_popup) { DestroyWindow(g_popup); return; }

    RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                 L"AppsUseLightTheme", RRF_RT_REG_DWORD, NULL, &light, &bytes);
    g_pal = light ? &PALETTE_LIGHT : &PALETTE_DARK;
    dark  = !light;

    /* Creata nascosta sotto il cursore, cosi' nasce col DPI di quel monitor. */
    GetCursorPos(&cursor);
    g_popup = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, L"MuteMePopup", APP_NAME, WS_POPUP,
                              cursor.x, cursor.y, 1, 1, NULL, NULL, g_inst, NULL);
    if (!g_popup) return;

    g_popupDpi = GetDpiForWindow(g_popup);
    if (g_popupDpi == 0) g_popupDpi = 96;
    g_fontBody  = PopupFont(14, FW_NORMAL);
    g_fontTitle = PopupFont(15, FW_SEMIBOLD);
    g_fontSmall = PopupFont(12, FW_NORMAL);

    /* Angoli arrotondati e bordo scuro su Windows 11; prima falliscono e basta. */
    DwmSetWindowAttribute(g_popup, DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(corner));
    DwmSetWindowAttribute(g_popup, DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(dark));

    g_cv.w  = Px(POPUP_W);
    size.cx = g_cv.w;
    size.cy = PopupLayout();
    CalculatePopupWindowPosition(&cursor, &size, TPM_RIGHTALIGN | TPM_BOTTOMALIGN | TPM_WORKAREA, NULL, &pos);
    SetWindowPos(g_popup, HWND_TOPMOST, pos.left, pos.top, size.cx, size.cy, SWP_SHOWWINDOW);
    SetForegroundWindow(g_popup);
}

static LRESULT CALLBACK PopupProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    const Hit *hit;

    switch (msg) {
    case WM_PAINT:
        PopupPaint(hwnd);
        return 0;

    case WM_ERASEBKGND:
        return 1;

    case WM_LBUTTONDOWN:
        hit = PopupHit(lParam);
        if (hit) PopupClick(hit, (short)LOWORD(lParam));
        return 0;

    case WM_LBUTTONUP:
        if (g_dragging) {
            g_dragging = FALSE;
            ReleaseCapture();
            SettingsSave();
            SfxPlay(2);
        }
        return 0;

    case WM_MOUSEMOVE: {
        TRACKMOUSEEVENT tme = { sizeof(tme), TME_LEAVE, 0, 0 };
        int hot;

        if (g_dragging) { PopupSlide((short)LOWORD(lParam)); return 0; }
        tme.hwndTrack = hwnd;
        TrackMouseEvent(&tme);
        hit = PopupHit(lParam);
        hot = hit && (hit->kind == HIT_KEY || hit->kind == HIT_QUIT) ? hit->kind : HIT_NONE;
        if (hot != g_hot) { g_hot = hot; InvalidateRect(hwnd, NULL, FALSE); }
        return 0;
    }

    case WM_MOUSELEAVE:
        if (g_hot) { g_hot = HIT_NONE; InvalidateRect(hwnd, NULL, FALSE); }
        return 0;

    /* Durante la registrazione Esc e' del thread input, che la annulla. */
    case WM_KEYDOWN:
        if (wParam == VK_ESCAPE && !g_recording) DestroyWindow(hwnd);
        return 0;

    case WM_ACTIVATE:
        if (LOWORD(wParam) == WA_INACTIVE) PostMessageW(hwnd, WM_CLOSE, 0, 0);
        break;

    case WM_DESTROY:
        g_popup    = NULL;
        g_hot      = HIT_NONE;
        g_dragging = FALSE;
        DeleteObject(g_fontBody);
        DeleteObject(g_fontTitle);
        DeleteObject(g_fontSmall);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

/* ----------------------------------------------------------------- raw input */

static BOOL IsModifierScanCode(int scanCode)
{
    /* Posizioni fisiche, identiche su ogni layout: Ctrl, LShift, RShift, Alt,
       tasti Windows e Menu (questi ultimi arrivano con prefisso E0). */
    return scanCode == 0x1D || scanCode == 0x2A || scanCode == 0x36 ||
           scanCode == 0x38 || scanCode == 0x5B || scanCode == 0x5C || scanCode == 0x5D;
}

static void HandleKey(int scanCode, BOOL extended, BOOL up)
{
    static BOOL  down;      /* l'hotkey e' fisicamente giu' */
    static DWORD lastMake;
    DWORD now = GetTickCount();
    BOOL repeat;

    if (g_recording) {
        if (up || IsModifierScanCode(scanCode)) return;

        g_recording = FALSE;
        if (scanCode != SCANCODE_ESC || extended) {   /* Esc annulla */
            g_cfg.hotkeyScanCode = scanCode;
            g_cfg.hotkeyExtended = extended;
            SfxPlay(2);
            down = TRUE;    /* il tasto appena registrato e' ancora premuto */
            lastMake = now;
        }
        PostMessageW(g_ctrl, WM_HOTKEY_SET, 0, 0);
        return;
    }

    if (scanCode != g_cfg.hotkeyScanCode || (extended ? 1 : 0) != g_cfg.hotkeyExtended) return;

    /* Finche' il tasto resta giu' il typematic rimanda il make: conta solo il
       primo, altrimenti una pressione appena lunga muta e smuta subito. Se il
       break si perde (desktop sicuro, KVM, tastiera scollegata) i make smettono
       di arrivare e dopo HOLD_TIMEOUT_MS il tasto torna valido. */
    repeat = down && now - lastMake < HOLD_TIMEOUT_MS;
    down = !up;
    if (up) return;
    lastMake = now;
    if (repeat) return;

    /* I modificatori richiesti devono essere giu'; quelli in piu' non contano,
       altrimenti l'hotkey sparisce mentre in gioco si tiene Shift o Ctrl. */
    if (g_cfg.requireShift && !(GetAsyncKeyState(VK_SHIFT)   & 0x8000)) return;
    if (g_cfg.requireCtrl  && !(GetAsyncKeyState(VK_CONTROL) & 0x8000)) return;
    if (g_cfg.requireAlt   && !(GetAsyncKeyState(VK_MENU)    & 0x8000)) return;

    ToggleMicrophone();
}

/* Chiamata anche dal watchdog: ri-registrarsi e' idempotente e non costa niente,
   e ci rimette in ascolto se la registrazione e' andata persa. */
static void RawInputRegister(void)
{
    RAWINPUTDEVICE rid;
    rid.usUsagePage = 0x01;  /* Generic Desktop */
    rid.usUsage     = 0x06;  /* Keyboard */
    rid.dwFlags     = RIDEV_INPUTSINK;
    rid.hwndTarget  = g_input;
    RegisterRawInputDevices(&rid, 1, sizeof(rid));
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

            /* Mai il riempitivo 0xFF che certe tastiere mandano dentro le
               sequenze escape. */
            if (kb->VKey != 0xFF) {
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

                HandleKey(scanCode, extended, (kb->Flags & RI_KEY_BREAK) != 0);
            }
        }
    }

done:
    if (buffer != stack) free(buffer);
}

/* ------------------------------------------------------------- thread input */

static LRESULT CALLBACK InputProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg) {
    case WM_INPUT:
        ProcessRawInput(lParam);
        return DefWindowProcW(hwnd, msg, wParam, lParam);

    case WM_TOGGLE:
        ToggleMicrophone();
        return 0;

    case WM_TIMER:
        RawInputRegister();
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

/* Priorita' TIME_CRITICAL: sopra ogni thread normale, anche quelli di un gioco
   che satura tutti i core. Il thread dorme in GetMessage e si sveglia solo a ogni
   tasto, quindi a riposo non ruba niente a nessuno. */
static DWORD WINAPI InputThread(void *ready)
{
    MSG msg;

    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    CoInitializeEx(NULL, COINIT_MULTITHREADED);

    g_input = CreateWindowExW(0, L"MuteMeInput", NULL, 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, g_inst, NULL);
    if (g_input) {
        RawInputRegister();
        SetTimer(g_input, WATCHDOG_TIMER, WATCHDOG_MS, NULL);
    }
    SetEvent((HANDLE)ready);
    if (!g_input) return 1;

    while (GetMessageW(&msg, NULL, 0, 0) > 0)
        DispatchMessageW(&msg);
    return 0;
}

/* ------------------------------------------------------------ window proc */

static LRESULT CALLBACK CtrlProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg) {
    case WM_TRAY:
        if (LOWORD(lParam) == WM_RBUTTONUP || LOWORD(lParam) == WM_CONTEXTMENU)
            PopupToggle();
        else if (LOWORD(lParam) == WM_LBUTTONDBLCLK)
            PostMessageW(g_input, WM_TOGGLE, 0, 0);
        return 0;

    case WM_MIC_CHANGED:
        OverlayRedraw();
        TrayUpdateIcon();
        if (g_popup) InvalidateRect(g_popup, NULL, FALSE);
        return 0;

    case WM_HOTKEY_SET:
        SettingsSave();
        if (g_popup) InvalidateRect(g_popup, NULL, FALSE);
        return 0;

    case WM_TIMER:
        TrayUpdateIcon();
        return 0;

    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}
/* Il cambio di DPI va gestito qui: WM_DPICHANGED arriva alla finestra che sta
   sul monitor, e il DPI che OverlayRedraw legge e' quello di questa finestra. */
static LRESULT CALLBACK OverlayProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg) {
    case WM_DPICHANGED:
    case WM_DISPLAYCHANGE:
    case WM_SETTINGCHANGE:
        OverlayRedraw();
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

/* ----------------------------------------------------------------- avvio */

static BOOL CreateWindows(void)
{
    WNDCLASSEXW wc;

    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = CtrlProc;
    wc.hInstance     = g_inst;
    wc.lpszClassName = L"MuteMeControl";
    if (!RegisterClassExW(&wc)) return FALSE;

    /* Finestra mai mostrata. WS_EX_TOOLWINDOW la tiene fuori da taskbar e alt-tab. */
    g_ctrl = CreateWindowExW(WS_EX_TOOLWINDOW, wc.lpszClassName, APP_NAME,
                             WS_POPUP, 0, 0, 0, 0, NULL, NULL, g_inst, NULL);
    if (!g_ctrl) return FALSE;

    wc.lpfnWndProc   = OverlayProc;
    wc.lpszClassName = L"MuteMeOverlay";
    if (!RegisterClassExW(&wc)) return FALSE;

    wc.lpfnWndProc   = InputProc;
    wc.lpszClassName = L"MuteMeInput";
    if (!RegisterClassExW(&wc)) return FALSE;

    wc.style         = CS_DROPSHADOW;
    wc.hCursor       = LoadCursorW(NULL, IDC_ARROW);
    wc.lpfnWndProc   = PopupProc;
    wc.lpszClassName = L"MuteMePopup";
    if (!RegisterClassExW(&wc)) return FALSE;

    g_overlay = CreateWindowExW(
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
        L"MuteMeOverlay", L"", WS_POPUP, 0, 0, 16, 16, NULL, NULL, g_inst, NULL);
    if (!g_overlay) return FALSE;

    /* Fuori da screenshot, registrazioni e condivisioni schermo. Il titolo vuoto
       la toglie anche dagli elenchi "condividi finestra", che saltano le finestre
       senza nome. Prima di Windows 10 2004 la chiamata fallisce e resta visibile. */
    SetWindowDisplayAffinity(g_overlay, WDA_EXCLUDEFROMCAPTURE);
    ShowWindow(g_overlay, SW_SHOWNOACTIVATE);

    SetTimer(g_ctrl, WATCHDOG_TIMER, WATCHDOG_MS, NULL);
    return TRUE;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE prev, PWSTR cmdLine, int show)
{
    MSG msg;
    HANDLE single, ready, input;

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

    ZeroMemory(&g_nid, sizeof(g_nid));
    g_nid.cbSize           = sizeof(g_nid);
    g_nid.hWnd             = g_ctrl;
    g_nid.uID              = 1;
    g_nid.uCallbackMessage = WM_TRAY;
    wcscpy_s(g_nid.szTip, ARRAYSIZE(g_nid.szTip), APP_NAME);

    AudioGetMute();
    g_autoStartOn = AutoStartActive();
    OverlayRedraw();
    TrayUpdateIcon();

    ready = CreateEventW(NULL, TRUE, FALSE, NULL);
    input = ready ? CreateThread(NULL, 0, InputThread, ready, 0, NULL) : NULL;
    if (!input) return 1;
    WaitForSingleObject(ready, INFINITE);
    CloseHandle(ready);
    CloseHandle(input);
    if (!g_input) return 1;

    /* Working set con minimo garantito: a riposo, con la memoria sotto pressione,
       Windows ci toglierebbe ogni pagina e la prima pressione dell'hotkey
       aspetterebbe il disco proprio mentre e' saturo. Sono pochi MB. */
    SetProcessWorkingSetSizeEx(GetCurrentProcess(), 16 << 20, 64 << 20,
                               QUOTA_LIMITS_HARDWS_MIN_ENABLE | QUOTA_LIMITS_HARDWS_MAX_DISABLE);

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
    CoUninitialize();
    return 0;
}
