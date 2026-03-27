using System.Diagnostics;
using System.Text.RegularExpressions;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;

namespace CyberWatch.Service.Services;

/// <summary>
/// Ejecuta <c>sc query</c> sobre el servicio Windows de CyberWatch y rellena los campos
/// <see cref="InstanciaMaquina"/> usados por el dashboard.
/// </summary>
public static class ServicioScQueryHelper
{
    public const int MaxServicioScSalidaChars = 2000;

    /// <summary>Ejecuta <c>sc query "NombreServicio"</c> y rellena campos para Firestore.</summary>
    public static void AplicarEstadoServicioDesdeScQuery(string serviceName, InstanciaMaquina instancia)
    {
        instancia.ServicioScConsultado = Timestamp.FromDateTime(DateTime.UtcNow);
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            instancia.ServicioScEstado = "SIN_NOMBRE";
            instancia.ServicioScDetalle = "App:ServiceName vacío en appsettings.";
            instancia.ServicioScSalida = null;
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("query");
            psi.ArgumentList.Add(serviceName);
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                instancia.ServicioScEstado = "ERROR_SC";
                instancia.ServicioScDetalle = "No se pudo iniciar sc.exe";
                instancia.ServicioScSalida = null;
                return;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(TimeSpan.FromSeconds(15));
            var combined = (stdout + "\n" + stderr).Trim();
            var exit = proc.ExitCode;

            if (exit != 0)
            {
                var lower = combined.ToLowerInvariant();
                if (combined.Contains("1060", StringComparison.Ordinal)
                    || lower.Contains("does not exist")
                    || lower.Contains("no existe el servicio")
                    || lower.Contains("specified service does not exist"))
                {
                    instancia.ServicioScEstado = "NO_EXISTE";
                    instancia.ServicioScDetalle = $"No hay servicio Windows llamado \"{serviceName}\" (revisá App:ServiceName vs sc create).";
                }
                else
                {
                    instancia.ServicioScEstado = "ERROR_SC";
                    instancia.ServicioScDetalle = $"sc.exe salió con código {exit}";
                }

                instancia.ServicioScSalida = TruncarServicioScSalida(combined);
                return;
            }

            var match = Regex.Match(
                combined,
                @"(?m)^\s*(STATE|ESTADO)\s*:\s*\d+\s+(.+)$",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var linea = match.Value.Trim();
                instancia.ServicioScDetalle = linea;
                instancia.ServicioScEstado = NormalizarEstadoServicioSc(linea);
            }
            else
            {
                instancia.ServicioScEstado = "DESCONOCIDO";
                instancia.ServicioScDetalle = "sc OK pero no se encontró línea STATE/ESTADO.";
            }

            instancia.ServicioScSalida = TruncarServicioScSalida(combined);
        }
        catch (Exception ex)
        {
            instancia.ServicioScEstado = "ERROR_SC";
            instancia.ServicioScDetalle = ex.Message;
            instancia.ServicioScSalida = null;
        }
    }

    private static string? TruncarServicioScSalida(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= MaxServicioScSalidaChars
            ? text
            : text[..MaxServicioScSalidaChars] + "...";
    }

    /// <summary>Valor estable para el dashboard (badges), a partir de la línea STATE/ESTADO de <c>sc</c> (EN/ES).</summary>
    private static string NormalizarEstadoServicioSc(string lineaState)
    {
        var u = lineaState.ToUpperInvariant();
        if (u.Contains("RUNNING")) return "RUNNING";
        if (u.Contains("STOPPED")) return "STOPPED";
        if (u.Contains("START_PENDING")) return "START_PENDING";
        if (u.Contains("STOP_PENDING")) return "STOP_PENDING";
        if (u.Contains("CONTINUE_PENDING")) return "CONTINUE_PENDING";
        if (u.Contains("PAUSE_PENDING")) return "PAUSE_PENDING";
        if (u.Contains("PAUSED")) return "PAUSED";
        if (u.Contains("EN EJECUCI")) return "RUNNING";
        if (u.Contains("DETENIDO") || u.Contains("PARADO")) return "STOPPED";
        if (u.Contains("PENDIENTE"))
        {
            if (u.Contains("INICIO") || u.Contains("START")) return "START_PENDING";
            if (u.Contains("DETEN") || u.Contains("STOP")) return "STOP_PENDING";
        }

        return "DESCONOCIDO";
    }
}
