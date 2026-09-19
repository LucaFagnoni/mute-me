#pragma once

/* Asset incorporati come RCDATA: nessun decoder a runtime. */
#define IDI_APPICON          100
#define IDR_OVERLAY_MUTED    101
#define IDR_OVERLAY_UNMUTED  102
#define IDR_TRAY_MUTED       103
#define IDR_TRAY_UNMUTED     104
#define IDR_WAV_MUTE         105
#define IDR_WAV_UNMUTE       106
#define IDR_WAV_NOTI         107
#define IDR_TASK_XML         108

/* Comandi di menu. Le voci a scelta multipla occupano un intervallo:
   il gestore ricava l'indice sottraendo la base. */
#define IDM_MUTE             200
#define IDM_VOLUME_BASE      210   /* +0..5  -> 0 20 40 60 80 100 % */
#define IDM_MODIFIER_BASE    220   /* +0..3  -> nessuno shift ctrl alt */
#define IDM_CORNER_BASE      230   /* +0..3  -> TL TR BL BR */
#define IDM_MARGIN_BASE      240   /* +0..5  -> 0 2 4 8 16 32 px */
#define IDM_HOTKEY           250
#define IDM_AUTOSTART        251
#define IDM_EXIT             252
