using Avalonia.Controls;
using Avalonia.Input;

namespace Mute_Me;

public partial class VolumePopup : Window
{
    public event Action<int>? VolumeChanged;

    public VolumePopup(int currentVolume)
    {
        InitializeComponent();

        var slider = this.FindControl<Slider>("VolSlider");
        if (slider != null)
        {
            slider.Value = currentVolume;

            slider.PropertyChanged += (_, e) => 
            {
                if (e.Property == Slider.ValueProperty)
                    VolumeChanged?.Invoke((int)slider.Value);
            };
        }

        // Chiudi la finestra se l'utente clicca fuori (perde il focus)
        this.Deactivated += (_, _) => this.Close();
        
        // Chiudi se preme ESC
        this.KeyDown += (_, e) => { if (e.Key == Key.Escape) this.Close(); };
    }
}
