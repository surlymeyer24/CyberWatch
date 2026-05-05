using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

/// <summary>
/// Documento en <c>cyberwatch_instancias/{machineId}/servicios_desconocidos/{nombreServicio}</c>.
/// Un documento por servicio no incluido en la whitelist base; se hace upsert (merge) en cada ciclo.
/// </summary>
[FirestoreData]
public class ServicioDesconocido
{
    /// <summary>Nombre corto del servicio (SCM ServiceName); suele coincidir con el ID del documento.</summary>
    [FirestoreProperty("nombre")]
    public string Nombre { get; set; } = "";

    [FirestoreProperty("nombreDisplay")]
    public string NombreDisplay { get; set; } = "";

    /// <summary>Estado del SCM, p. ej. Running, Stopped.</summary>
    [FirestoreProperty("estado")]
    public string Estado { get; set; } = "";

    /// <summary>Ruta del ejecutable desde ImagePath del registro (preferiblemente ya expandida).</summary>
    [FirestoreProperty("rutaEjecutable")]
    public string RutaEjecutable { get; set; } = "";

    /// <summary>Tipo de inicio, p. ej. Automatic, Manual, Disabled.</summary>
    [FirestoreProperty("tipoInicio")]
    public string TipoInicio { get; set; } = "";

    [FirestoreProperty("machineId")]
    public string MachineId { get; set; } = "";

    [FirestoreProperty("hostname")]
    public string Hostname { get; set; } = "";

    /// <summary>Última vez que el monitor evaluó y persistió este servicio.</summary>
    [FirestoreProperty("fechaDeteccion")]
    public Timestamp FechaDeteccion { get; set; }

    /// <summary>
    /// <see langword="true"/> si el servicio apareció como no-base en este ciclo y no estaba en el conjunto de no-base del ciclo anterior.
    /// </summary>
    [FirestoreProperty("esNuevo")]
    public bool EsNuevo { get; set; }
}

/// <summary>Valor de <see cref="Alerta.Tipo"/> al escribir en la subcolección <c>alertas</c>.</summary>
public static class ServicioDesconocidoTipoAlerta
{
    public const string NuevoEntreCiclos = "servicio_desconocido_nuevo";
}

/// <summary>Valor de <see cref="Alerta.Tipo"/> para alertas de firma Authenticode en servicios Windows (Service).</summary>
public static class ServicioFirmaDigitalTipoAlerta
{
    public const string SinFirmaValida = "servicio_sin_firma_valida";
}
