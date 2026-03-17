using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

[FirestoreData]
public class EntradaHistorial
{
    [FirestoreProperty("url")]
    public string Url { get; set; } = "";

    [FirestoreProperty("titulo")]
    public string? Titulo { get; set; }

    [FirestoreProperty("fecha_visita")]
    public Timestamp FechaVisita { get; set; }

    [FirestoreProperty("navegador")]
    public string Navegador { get; set; } = "";

    [FirestoreProperty("perfil")]
    public string? Perfil { get; set; }

    [FirestoreProperty("sincronizado")]
    public Timestamp Sincronizado { get; set; }
}
