using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Avalonia.Platform;

namespace Mute_Me;

public class AutoStartManager
{
    private const string TASK_NAME = "MuteMe_AutoStart";

    private string GetSchTasksPath()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        
        string sysNative = Path.Combine(root, "Windows", "Sysnative", "schtasks.exe");
        if (File.Exists(sysNative)) return sysNative;

        string sys32 = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        if (File.Exists(sys32)) return sys32;

        return "schtasks.exe";
    }

    private async Task<int> RunSchTasksAsync(string arguments)
    {
        string exePath = GetSchTasksPath();
        
        return await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(startInfo);
                if (process == null) return -1;
                process.WaitForExit();
                return process.ExitCode;
            }
            catch { return -1; }
        });
    }

    public async Task<bool> IsStartupTaskActiveAsync()
    {
        int exitCode = await RunSchTasksAsync($"/Query /TN \"{TASK_NAME}\" /FO CSV");
        return exitCode == 0;
    }

    public async Task InstallStartupTaskAsync()
    {
        if (!IsAdministrator()) return;

        string taskXmlTemplate = LoadXmlFromAssets();
        if (string.IsNullOrEmpty(taskXmlTemplate)) return;

        string appExePath = Process.GetCurrentProcess().MainModule!.FileName;
        string safePath = System.Security.SecurityElement.Escape(appExePath); // Escape XML caratteri speciali
        
        string finalXml = taskXmlTemplate.Replace("{EXE_PATH}", safePath);

        string tempXml = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempXml, finalXml, Encoding.Unicode);
            await RunSchTasksAsync($"/Create /TN \"{TASK_NAME}\" /XML \"{tempXml}\" /F");
        }
        catch { }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
        }
    }

    public async Task UninstallStartupTaskAsync()
    {
        await RunSchTasksAsync($"/Delete /TN \"{TASK_NAME}\" /F");
    }

    private string LoadXmlFromAssets()
    {
        try
        {
            var uri = new Uri($"avares://MuteMe/Assets/TaskConfig.xml");

            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}