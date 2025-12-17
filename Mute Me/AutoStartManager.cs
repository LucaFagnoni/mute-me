using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;

public class AutoStartManager
{
    private const string TASK_NAME = "MuteMe_AutoStart";
    
    // Percorso del log dedicato per evitare conflitti con MainWindow
    private string _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AutoStart_Log.txt");

    private void Log(string msg)
    {
        try 
        {
            File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss} - {msg}{Environment.NewLine}");
        } 
        catch { /* Ignora errori di IO del log */ }
    }

    // Metodo per trovare schtasks.exe senza errori
    private string? GetSchTasksPath()
    {
        // 1. Prova con Environment.SystemDirectory (solitamente C:\Windows\System32)
        string p1 = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        if (File.Exists(p1)) return p1;

        // 2. Prova hardcoded (nel caso l'environment sia strano)
        string p2 = @"C:\Windows\System32\schtasks.exe";
        if (File.Exists(p2)) return p2;

        // 3. Prova nella cartella Windows principale
        string p3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "schtasks.exe");
        if (File.Exists(p3)) return p3;

        Log("ERRORE CRITICO: schtasks.exe non trovato in nessuna posizione standard.");
        return null;
    }

    public async Task<bool> IsStartupTaskActiveAsync()
    {
        // Logga l'inizio
        Log("IsStartupTaskActiveAsync avviato.");

        string? exePath = GetSchTasksPath();
        if (exePath == null) return false;

        return await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    // Aggiungiamo FO per formattare l'output ed evitare errori di parsing strani
                    Arguments = $"/Query /TN \"{TASK_NAME}\" /FO CSV", 
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true // Catturiamo anche l'errore per non farlo esplodere
                };

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                // Leggiamo tutto ma non facciamo nulla con l'errore
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
            
                process.WaitForExit();
            
                // Se ExitCode è 0, il task esiste.
                // Se è 1, il task non esiste (comportamento normale, non un crash).
                Log($"ExitCode: {process.ExitCode}");
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                // Se arriviamo qui, è un errore reale di sistema (es. permessi), non "File not found"
                Log($"ECCEZIONE REALE: {ex.Message}");
                return false;
            }
        });
    }

    public async Task InstallStartupTaskAsync()
    {
        Log("InstallStartupTaskAsync avviato.");
        
        string? exePath = GetSchTasksPath();
        if (exePath == null) return;
        
        if (!IsAdministrator()) 
        {
            Log("Non sono Admin. Annullato.");
            return;
        }

        string appExePath = Process.GetCurrentProcess().MainModule.FileName;
        
        string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>
  <Actions Context=""Author""><Exec><Command>{System.Security.SecurityElement.Escape(appExePath)}</Command></Exec></Actions>
</Task>";

        string tempXml = Path.GetTempFileName();
        File.WriteAllText(tempXml, taskXml);

        await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"/Create /TN \"{TASK_NAME}\" /XML \"{tempXml}\" /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(startInfo);
                proc?.WaitForExit();
                Log("Task creato (forse).");
            }
            catch (Exception ex) { Log($"Install Error: {ex.Message}"); }
            finally
            {
                if (File.Exists(tempXml)) File.Delete(tempXml);
            }
        });
    }

    public async Task UninstallStartupTaskAsync()
    {
        string? exePath = GetSchTasksPath();
        if (exePath == null) return;

        await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"/Delete /TN \"{TASK_NAME}\" /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(startInfo);
                proc?.WaitForExit();
            }
            catch { }
        });
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