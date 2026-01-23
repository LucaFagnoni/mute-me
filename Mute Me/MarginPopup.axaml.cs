using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace Mute_Me;

public partial class MarginPopup : Window
{
    public event Action<int>? MarginChanged;

    public MarginPopup(int currentMargin)
    {
        InitializeComponent();

        var slider = this.FindControl<Slider>("MarginSlider");
        if (slider != null)
        {
            slider.Value = currentMargin;

            slider.PropertyChanged += (_, e) => 
            {
                if (e.Property == Slider.ValueProperty)
                    MarginChanged?.Invoke((int)slider.Value);
            };
        }

        this.Deactivated += (_, _) => this.Close();
        this.KeyDown += (_, e) => { if (e.Key == Key.Escape) this.Close(); };
    }
}
