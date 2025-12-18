using Avalonia;
using System;
using System.IO;

namespace Mute_Me;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // QUESTO È FONDAMENTALE PER CAPIRE PERCHÉ LA RELEASE NON PARTE
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MuteMe_CRASH.txt");
            string error = $"[{DateTime.Now}] CRASH FATALE:\n{ex.Message}\n\nSTACK TRACE:\n{ex.StackTrace}";
            
            if (ex.InnerException != null)
                error += $"\n\nINNER EXCEPTION:\n{ex.InnerException.Message}";
                
            File.WriteAllText(path, error);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}