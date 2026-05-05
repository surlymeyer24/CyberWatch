using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace CyberWatch.Service.Services;

/// <summary>
/// Registra/ejecuta la tarea programada <c>CyberWatch\UserAgent</c> (misma lógica que el post-actualización).
/// </summary>
[SupportedOSPlatform("windows")]
public static class LanzadorUserAgent
{
    public const string NombreTarea = @"CyberWatch\UserAgent";

    /// <summary>
    /// Fuerza <c>schtasks /Create /F</c> + <c>/Run</c> para relanzar el UserAgent en sesión de usuario.
    /// </summary>
    public static string RelanzarDesdeTarea(string exePath, ILogger logger)
    {
        var result = "";
        try
        {
            if (!File.Exists(exePath))
            {
                logger.LogWarning("[Watchdog] {Exe} no existe en {Dir}.", Path.GetFileName(exePath), Path.GetDirectoryName(exePath));
                return "Exe no encontrado.";
            }

            var xml = RegistradorTareaUsuarioService.GenerarXmlTarea(exePath);
            var xmlPath = Path.Combine(Path.GetTempPath(), "cyberwatch_useragent_task.xml");
            File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

            try
            {
                logger.LogInformation("[Watchdog] Registrando tarea UserAgent (forzado)...");
                var (creado, msgCreate) = EjecutarProceso("schtasks",
                    $"/Create /TN \"{NombreTarea}\" /XML \"{xmlPath}\" /F");
                logger.LogInformation("[Watchdog] schtasks /Create: ok={Ok}, output={Msg}", creado, msgCreate);
                result += creado ? "Tarea registrada OK. " : $"Tarea no registrada: {msgCreate}. ";
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { /* ignore */ }
            }

            logger.LogInformation("[Watchdog] Ejecutando tarea UserAgent...");
            var (ejecutado, msgRun) = EjecutarProceso("schtasks", $"/Run /TN \"{NombreTarea}\"");
            logger.LogInformation("[Watchdog] schtasks /Run: ok={Ok}, output={Msg}", ejecutado, msgRun);
            result += ejecutado ? "UserAgent iniciado OK." : $"UserAgent no iniciado: {msgRun}.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Watchdog] Error al relanzar UserAgent.");
            result += $"Error: {ex.Message}";
        }

        return result;
    }

    private static (bool success, string output) EjecutarProceso(string fileName, string args)
    {
        var info = new ProcessStartInfo(fileName, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(info);
        var stdout = proc?.StandardOutput.ReadToEnd() ?? "";
        var stderr = proc?.StandardError.ReadToEnd() ?? "";
        proc?.WaitForExit();
        return (proc?.ExitCode == 0, $"{stdout}{stderr}".Trim());
    }
}
