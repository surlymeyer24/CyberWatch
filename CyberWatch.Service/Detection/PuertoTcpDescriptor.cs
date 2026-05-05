namespace CyberWatch.Service.Detection;

/// <summary>Fila TCP interpretada para telemetría (IPv4).</summary>
public sealed class PuertoTcpDescriptor
{
    public required string EstadoTexto { get; init; }
    public required uint EstadoRaw { get; init; }
    public required string IpLocal { get; init; }
    public required ushort PuertoLocal { get; init; }
    public required string IpRemota { get; init; }
    public required ushort PuertoRemoto { get; init; }
    public required int Pid { get; init; }
    public required string NombreProceso { get; init; }
    public required string RutaProceso { get; init; }
}
