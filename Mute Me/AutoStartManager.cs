using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;

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
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Environment.SystemDirectory
                };

                // Redirect the output to prevent the process from crashing if the buffer fills up.
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                using var process = Process.Start(startInfo);
                if (process == null) return -1;

                // Read to empty the buffers (even if we don't use the text)
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Win32Exception)
            {
                // This specifically catches “File not found” if schtasks is missing.
                return -1;
            }
            catch
            {
                return -1;
            }
        });
    }

    public async Task<bool> IsStartupTaskActiveAsync()
    {
        // /Query returns 0 if it finds the task, 1 if it does not find it.
        int exitCode = await RunSchTasksAsync($"/Query /TN \"{TASK_NAME}\" /FO CSV");
        return exitCode == 0;
    }

    public async Task InstallStartupTaskAsync()
    {
        if (!IsAdministrator()) return;

        // 1. USA ENVIRONMENT.PROCESSPATH (Fondamentale per Native AOT / Single File)
        string? appExePath = Environment.ProcessPath;
    
        if (string.IsNullOrEmpty(appExePath))
        {
            // Fallback estremo se ProcessPath è nullo
            appExePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrEmpty(appExePath)) return;

        // 2. Ottieni la cartella di lavoro reale
        string workingDir = Path.GetDirectoryName(appExePath) ?? "";

        string schTasksExe = GetSchTasksPath();

        // 3. Costruzione XML con Escape rigoroso
        string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{appExePath}""</Command>
      <WorkingDirectory>""{workingDir}""</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

        // Notare le virgolette aggiuntive "" attorno a appExePath nell'XML sopra.

        string tempXml = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempXml, taskXml);
            await RunSchTasksAsync($"/Create /TN \"{TASK_NAME}\" /XML \"{tempXml}\" /F");
        }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
        }
    }

    public async Task UninstallStartupTaskAsync()
    {
        await RunSchTasksAsync($"/Delete /TN \"{TASK_NAME}\" /F");
    }

    private bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}