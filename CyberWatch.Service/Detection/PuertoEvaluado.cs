namespace CyberWatch.Service.Detection;

public sealed class PuertoEvaluado
{
    public required PuertoTcpDescriptor Descriptor { get; init; }
    public bool EsSospechoso { get; init; }
    public bool EsNuevoEntreCiclos { get; init; }
}
