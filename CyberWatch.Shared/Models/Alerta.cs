using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

/// <summary>
/// Documento de la colección "alertas". Cubre distintos orígenes:
/// - ransomware: NombreProceso, EscriturasSospechosas, RenombradosSospechosas, ExtensionSospechosa
/// - evento de seguridad: Tipo, EventoId, Descripcion, Detalle
/// - servicios no-base: Tipo = <c>servicio_desconocido_nuevo</c>, NombreServicio, opcionalmente flags en Detalle
/// - firma Authenticode: Tipo = <c>servicio_sin_firma_valida</c>, NombreServicio, SubjectFirma, RazonFirma, RutaEjecutableOriginal
/// - servicio anómalo (política remota): Tipo = <c>servicio_no_firmado</c>, HashEjecutableSha256 si aplica
/// - puertos TCP: Tipo = valores en <see cref="PuertoTipoAlerta"/> + PuertoLocal, PidProceso, Descripcion
/// Los campos no usados por cada tipo quedan en null.
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

    [FirestoreProperty("extensionDetectada")]
    public string? ExtensionDetectada { get; set; }

    // ── Cuarentena ────────────────────────────────────────────────────────────

    [FirestoreProperty("rutaEjecutableOriginal")]
    public string? RutaEjecutableOriginal { get; set; }

    [FirestoreProperty("rutaCuarentena")]
    public string? RutaCuarentena { get; set; }

    [FirestoreProperty("cuarentenaExitosa")]
    public bool? CuarentenaExitosa { get; set; }

    [FirestoreProperty("cuarentenaError")]
    public string? CuarentenaError { get; set; }

    // ── Alerta de evento de seguridad (SecurityEventMonitorService) ───────────

    [FirestoreProperty("tipo")]
    public string? Tipo { get; set; }

    [FirestoreProperty("eventoId")]
    public int? EventoId { get; set; }

    [FirestoreProperty("descripcion")]
    public string? Descripcion { get; set; }

    [FirestoreProperty("detalle")]
    public string? Detalle { get; set; }

    // ── Monitor de servicios no-base (ServiciosDesconocidosService) ───────────

    /// <summary>Cuando <see cref="Tipo"/> es <c>servicio_desconocido_nuevo</c>, nombre corto SCM del servicio.</summary>
    [FirestoreProperty("nombreServicio")]
    public string? NombreServicio { get; set; }

    // ── Monitor de puertos (PuertosAbiertosMonitorService) ─────────────────────

    /// <summary>Puerto local cuando <see cref="Tipo"/> es <see cref="PuertoTipoAlerta"/>.</summary>
    [FirestoreProperty("puertoLocal")]
    public int? PuertoLocal { get; set; }

    /// <summary>PID del proceso dueño del socket.</summary>
    [FirestoreProperty("pidProceso")]
    public int? PidProceso { get; set; }

    /// <summary>Solo alertas ransomware: entropía de muestra si el bonus aplicó.</summary>
    [FirestoreProperty("entropiaMuestra")]
    public double? EntropiaMuestra { get; set; }

    [FirestoreProperty("entropiaAplicadaComoBonus")]
    public bool EntropiaAplicadaComoBonus { get; set; }

    /// <summary>Cuando <see cref="Tipo"/> es firma de servicio: sujeto del certificado de firma (si hubo lectura).</summary>
    [FirestoreProperty("subjectFirma")]
    public string? SubjectFirma { get; set; }

    /// <summary>Razón codificada: sin_firma, cadena_invalida, error_lectura.</summary>
    [FirestoreProperty("razonFirma")]
    public string? RazonFirma { get; set; }

    /// <summary>Cuando <see cref="Tipo"/> es <c>servicio_no_firmado</c>: SHA-256 del binario (hex minúsculas).</summary>
    [FirestoreProperty("hashEjecutableSha256")]
    public string? HashEjecutableSha256 { get; set; }
}
