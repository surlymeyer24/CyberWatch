using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

/// <summary>
/// Documento de la colección "alertas". Cubre dos tipos:
/// - ransomware: NombreProceso, EscriturasSospechosas, RenombradosSospechosas, ExtensionSospechosa
/// - evento de seguridad: Tipo, EventoId, Descripcion, Detalle
/// Los campos específicos del otro tipo quedan en null y no afectan las queries.
/// </summary>
[FirestoreData]
public class Alerta
{
    // ── Campos comunes ────────────────────────────────────────────────────────

    [FirestoreProperty("fechaHora")]
    public Timestamp FechaHora { get; set; }

    [FirestoreProperty("origen")]
    public string Origen { get; set; } = "";

    [FirestoreProperty("machineId")]
    public string MachineId { get; set; } = "";

    [FirestoreProperty("hostname")]
    public string Hostname { get; set; } = "";

    // ── Alerta de ransomware (FirebaseAlertService) ───────────────────────────

    [FirestoreProperty("nombreProceso")]
    public string? NombreProceso { get; set; }

    [FirestoreProperty("escriturasSospechosas")]
    public bool? EscriturasSospechosas { get; set; }

    [FirestoreProperty("renombradosSospechosas")]
    public bool? RenombradosSospechosas { get; set; }

    [FirestoreProperty("extensionSospechosa")]
    public bool? ExtensionSospechosa { get; set; }

    // ── Alerta de evento de seguridad (SecurityEventMonitorService) ───────────

    [FirestoreProperty("tipo")]
    public string? Tipo { get; set; }

    [FirestoreProperty("eventoId")]
    public int? EventoId { get; set; }

    [FirestoreProperty("descripcion")]
    public string? Descripcion { get; set; }

    [FirestoreProperty("detalle")]
    public string? Detalle { get; set; }
}
