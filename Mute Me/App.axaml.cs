using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;

namespace Mute_Me;

public partial class App : Application
{
    public ICommand SetModifier { get; set; }
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        SetModifier = new RelayCommand<IntPtr>(SetModifierCommand);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void SetModifierCommand(IntPtr modifier)
    {
        
    }

    private void Quit_OnClick(object? sender, EventArgs e)
    {
        Environment.Exit(0);
    }
}