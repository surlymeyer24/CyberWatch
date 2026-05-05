using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

/// <summary>
/// Documento en <c>cyberwatch_instancias/{machineId}/puertos_abiertos/{id}</c>.
/// Un documento por fila monitoreada; merge en cada ciclo del monitor de puertos del Service.
/// </summary>
[FirestoreData]
public class PuertoAbierto
{
    /// <summary>TCP o UDP.</summary>
    [FirestoreProperty("protocolo")]
    public string Protocolo { get; set; } = "TCP";

    /// <summary>Estado MIB de la fila (Listen, Established, …).</summary>
    [FirestoreProperty("estadoTcp")]
    public string EstadoTcp { get; set; } = "";

    /// <summary>Puerto local en orden host.</summary>
    [FirestoreProperty("puertoLocal")]
    public int PuertoLocal { get; set; }

    /// <summary>Dirección IPv4 local textual.</summary>
    [FirestoreProperty("ipLocal")]
    public string IpLocal { get; set; } = "";

    /// <summary>Dirección remota (vacío si no aplica).</summary>
    [FirestoreProperty("ipRemota")]
    public string IpRemota { get; set; } = "";

    /// <summary>Puerto remoto en orden host (0 si no aplica).</summary>
    [FirestoreProperty("puertoRemoto")]
    public int PuertoRemoto { get; set; }

    [FirestoreProperty("pid")]
    public int Pid { get; set; }

    [FirestoreProperty("nombreProceso")]
    public string NombreProceso { get; set; } = "";

    [FirestoreProperty("rutaProceso")]
    public string RutaProceso { get; set; } = "";

    [FirestoreProperty("machineId")]
    public string MachineId { get; set; } = "";

    [FirestoreProperty("hostname")]
    public string Hostname { get; set; } = "";

    [FirestoreProperty("fechaDeteccion")]
    public Timestamp FechaDeteccion { get; set; }

    /// <summary>Puerto incluido en la lista negra embebida.</summary>
    [FirestoreProperty("esSospechoso")]
    public bool EsSospechoso { get; set; }

    /// <summary>
    /// True si la clave (estado:puerto:pid) no estaba en el ciclo anterior.
    /// </summary>
    [FirestoreProperty("esNuevo")]
    public bool EsNuevo { get; set; }
}

/// <summary>Valores de <see cref="Alerta.Tipo"/> para el monitor de puertos.</summary>
public static class PuertoTipoAlerta
{
    public const string Sospechoso = "puerto_sospechoso";
    public const string NuevoEntreCiclos = "puerto_nuevo_entre_ciclos";
}
