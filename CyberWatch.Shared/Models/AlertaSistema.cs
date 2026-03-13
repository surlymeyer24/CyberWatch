using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

/// <summary>
/// Resumen de alerta embebido en el array alertas_sistema[] dentro de cyberwatch_instancias.
/// Guarda solo los campos necesarios para mostrar en el Dashboard.
/// </summary>
[FirestoreData]
public class AlertaSistema
{
    [FirestoreProperty("tipo")]
    public string Tipo { get; set; } = "";

    [FirestoreProperty("fechaHora")]
    public string FechaHora { get; set; } = "";

    [FirestoreProperty("descripcion")]
    public string Descripcion { get; set; } = "";
}
