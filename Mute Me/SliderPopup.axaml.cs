using Avalonia.Controls;
using Avalonia.Input;

namespace Mute_Me;

public partial class SliderPopup : Window
{
    public event Action<int>? ValueChanged;

    public SliderPopup(string label, int value, int tickFrequency, string suffix)
    {
        InitializeComponent();

        LabelText.Text = label;
        ValueText.Text = value + suffix;
        ValueSlider.TickFrequency = tickFrequency;
        ValueSlider.Value = value;

        ValueSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            int v = (int)ValueSlider.Value;
            ValueText.Text = v + suffix;
            ValueChanged?.Invoke(v);
        };

        Deactivated += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
