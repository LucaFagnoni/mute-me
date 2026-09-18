using System.Diagnostics;
using System.Security;
using System.Text;
using Avalonia.Platform;

namespace Mute_Me;

public class AutoStartManager
{
    private const string TASK_NAME = "MuteMe_AutoStart";

    // Il manifest impone requireAdministrator, quindi il processo e' sempre elevato
    // e sempre a 64 bit: niente check admin, niente probing di Sysnative.
    private static Task<int> RunSchTasksAsync(string arguments) => Task.Run(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (process == null) return -1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch { return -1; }
    });

    public async Task<bool> IsStartupTaskActiveAsync() =>
        await RunSchTasksAsync($"/Query /TN \"{TASK_NAME}\" /FO CSV") == 0;

    public async Task InstallStartupTaskAsync()
    {
        string template = LoadXmlFromAssets();
        if (string.IsNullOrEmpty(template)) return;

        string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
        string finalXml = template.Replace("{EXE_PATH}", SecurityElement.Escape(exePath));

        string tempXml = System.IO.Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempXml, finalXml, Encoding.Unicode);
            await RunSchTasksAsync($"/Create /TN \"{TASK_NAME}\" /XML \"{tempXml}\" /F");
        }
        catch { }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
        }
    }

    public Task<int> UninstallStartupTaskAsync() => RunSchTasksAsync($"/Delete /TN \"{TASK_NAME}\" /F");

    private static string LoadXmlFromAssets()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://MuteMe/Assets/TaskConfig.xml"));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch { return string.Empty; }
    }
}
