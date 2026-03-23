using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

/// <summary>
/// Registro append-only en <c>cyberwatch_instancias/{machineId}/logs_amenazas</c>.
/// Cada detección de posible ransomware genera un documento (aunque la alerta en <c>alertas</c> se omita por deduplicación).
/// </summary>
[FirestoreData]
public class LogAmenaza
{
    [FirestoreProperty("fechaHora")]
    public Timestamp FechaHora { get; set; }

    [FirestoreProperty("machineId")]
    public string MachineId { get; set; } = "";

    [FirestoreProperty("hostname")]
    public string Hostname { get; set; } = "";

    /// <summary>
    /// true si además se creó documento en la subcolección <c>alertas</c>; false si se omitió por deduplicación (mismo proceso en ventana reciente).
    /// </summary>
    [FirestoreProperty("alertaFirestoreCreada")]
    public bool AlertaFirestoreCreada { get; set; }

    [FirestoreProperty("nombreProceso")]
    public string? NombreProceso { get; set; }

    [FirestoreProperty("escriturasSospechosas")]
    public bool EscriturasSospechosas { get; set; }

    [FirestoreProperty("renombradosSospechosas")]
    public bool RenombradosSospechosas { get; set; }

    [FirestoreProperty("extensionSospechosa")]
    public bool ExtensionSospechosa { get; set; }

    [FirestoreProperty("extensionDetectada")]
    public string? ExtensionDetectada { get; set; }

    [FirestoreProperty("rutaEjecutableOriginal")]
    public string? RutaEjecutableOriginal { get; set; }

    [FirestoreProperty("cuarentenaExitosa")]
    public bool? CuarentenaExitosa { get; set; }

    [FirestoreProperty("rutaCuarentena")]
    public string? RutaCuarentena { get; set; }

    [FirestoreProperty("cuarentenaError")]
    public string? CuarentenaError { get; set; }

    /// <summary>
    /// Cantidad de eventos de archivo en el ciclo de monitorización atribuidos a este proceso.
    /// </summary>
    [FirestoreProperty("eventosArchivoEnCiclo")]
    public int EventosArchivoEnCiclo { get; set; }

    /// <summary>
    /// Texto legible para consultas rápidas en consola / dashboard.
    /// </summary>
    [FirestoreProperty("resumen")]
    public string Resumen { get; set; } = "";
}
