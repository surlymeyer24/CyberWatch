using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberWatch.Service.Services;

/// <summary>
/// Al arrancar el Service, registra CyberWatch.UserAgent.exe en el Task Scheduler
/// para que se ejecute automáticamente al iniciar sesión cualquier usuario.
/// </summary>
public class RegistradorTareaUsuarioService : BackgroundService
{
    private const string NombreTarea = @"CyberWatch\UserAgent";
    private const string NombreExe = "CyberWatch.UserAgent.exe";

    private readonly ILogger<RegistradorTareaUsuarioService> _logger;

    public RegistradorTareaUsuarioService(ILogger<RegistradorTareaUsuarioService> logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var exePath = Path.Combine(AppContext.BaseDirectory, NombreExe);
            if (!File.Exists(exePath))
            {
                _logger.LogDebug("{Exe} no encontrado en {Dir}. Tarea de usuario no registrada.", NombreExe, AppContext.BaseDirectory);
                return Task.CompletedTask;
            }

            if (TareaExiste())
            {
                _logger.LogDebug("Tarea '{Tarea}' ya registrada.", NombreTarea);
                return Task.CompletedTask;
            }

            RegistrarTarea(exePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo registrar la tarea del UserAgent en Task Scheduler.");
        }

        return Task.CompletedTask;
    }

    private static bool TareaExiste()
    {
        var info = new ProcessStartInfo("schtasks", $"/Query /TN \"{NombreTarea}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(info);
        proc?.WaitForExit();
        return proc?.ExitCode == 0;
    }

    private void RegistrarTarea(string exePath)
    {
        var xml = GenerarXmlTarea(exePath);
        var xmlPath = Path.Combine(Path.GetTempPath(), "cyberwatch_useragent_task.xml");
        File.WriteAllText(xmlPath, xml, Encoding.Unicode); // schtasks requiere UTF-16

        try
        {
            var info = new ProcessStartInfo("schtasks", $"/Create /TN \"{NombreTarea}\" /XML \"{xmlPath}\" /F")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(info);
            proc?.WaitForExit();

            if (proc?.ExitCode == 0)
                _logger.LogInformation("Tarea '{Tarea}' registrada en Task Scheduler.", NombreTarea);
            else
                _logger.LogWarning("schtasks retornó código {Code} al registrar la tarea.", proc?.ExitCode);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static string GenerarXmlTarea(string exePath) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <GroupId>S-1-5-32-545</GroupId>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Hidden>true</Hidden>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{exePath}</Command>
            </Exec>
          </Actions>
        </Task>
        """;
}
