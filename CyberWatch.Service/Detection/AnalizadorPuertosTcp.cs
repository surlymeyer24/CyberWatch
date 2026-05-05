using System.Diagnostics;
using CyberWatch.Service.Interop;

namespace CyberWatch.Service.Detection;

/// <summary>
/// Convierte la tabla extendida TCP del kernel en descriptores con nombre/ruta de proceso.
/// </summary>
public static class AnalizadorPuertosTcp
{
    /// <summary>
    /// Enumera sockets TCP IPv4. Si <paramref name="soloListen"/> es true, solo filas en estado LISTEN.
    /// </summary>
    public static IReadOnlyList<PuertoTcpDescriptor> Enumerar(bool soloListen)
    {
        var raw = IpHelperTcpTable.EnumerarTcp4();
        var lista = new List<PuertoTcpDescriptor>(raw.Count);

        foreach (var row in raw)
        {
            if (soloListen && row.dwState != IpHelperTcpTable.MIB_TCP_STATE_LISTEN)
                continue;

            var puertoLocal = IpHelperTcpTable.NetworkOrderPortFieldToHost(row.dwLocalPort);
            var puertoRemoto = IpHelperTcpTable.NetworkOrderPortFieldToHost(row.dwRemotePort);

            var pid = (int)row.dwOwningPid;
            string nombre = "";
            string ruta = "";

            if (pid > 0)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    nombre = p.ProcessName ?? "";
                    try { ruta = p.MainModule?.FileName ?? ""; }
                    catch { /* sin permisos o proceso ya terminó */ }
                }
                catch (ArgumentException) { /* PID inválido */ }
                catch (InvalidOperationException) { }
            }

            lista.Add(new PuertoTcpDescriptor
            {
                EstadoRaw = row.dwState,
                EstadoTexto = EstadoTcpTexto(row.dwState),
                IpLocal = IpHelperTcpTable.FormatIpv4(row.dwLocalAddr),
                PuertoLocal = puertoLocal,
                IpRemota = IpHelperTcpTable.FormatIpv4(row.dwRemoteAddr),
                PuertoRemoto = puertoRemoto,
                Pid = pid,
                NombreProceso = nombre,
                RutaProceso = ruta
            });
        }

        return lista;
    }

    private static string EstadoTcpTexto(uint estado)
    {
        return estado switch
        {
            1 => "Closed",
            2 => "Listen",
            3 => "SynSent",
            4 => "SynReceived",
            5 => "Established",
            6 => "FinWait1",
            7 => "FinWait2",
            8 => "CloseWait",
            9 => "Closing",
            10 => "LastAck",
            11 => "TimeWait",
            12 => "DeleteTcb",
            _ => $"State_{estado}"
        };
    }
}
