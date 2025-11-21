using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;

namespace Mute_Me;

public partial class VolumePopup : Window
{
    // Evento per notificare il cambio volume alla MainWindow
    public event Action<int>? VolumeChanged;

    public VolumePopup(int currentVolume)
    {
        InitializeComponent();
        
        // Imposta il valore iniziale
        var slider = this.FindControl<Slider>("VolSlider");
        if (slider != null)
        {
            slider.Value = currentVolume;
            // Quando lo slider cambia, invoca l'evento
            slider.PropertyChanged += (s, e) => 
            {
                if (e.Property == Slider.ValueProperty)
                    VolumeChanged?.Invoke((int)slider.Value);
            };
        }

        // Chiudi la finestra se l'utente clicca fuori (perde il focus)
        this.Deactivated += (s, e) => this.Close();
        
        // Chiudi se preme ESC
        this.KeyDown += (s, e) => { if (e.Key == Key.Escape) this.Close(); };
    }
}
