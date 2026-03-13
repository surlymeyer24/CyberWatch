using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Helpers;

/// <summary>
/// Centraliza la creación de FirestoreDb para evitar duplicar la inicialización del builder.
/// </summary>
public static class FirestoreDbFactory
{
    /// <summary>
    /// Crea un FirestoreDb. Si se proporciona credentialsPath usa FirestoreDbBuilder;
    /// de lo contrario usa las credenciales de entorno (GOOGLE_APPLICATION_CREDENTIALS).
    /// </summary>
    public static FirestoreDb Create(string projectId, string? credentialsPath = null)
    {
        if (!string.IsNullOrEmpty(credentialsPath))
            return new FirestoreDbBuilder
            {
                ProjectId = projectId,
                CredentialsPath = credentialsPath
            }.Build();

        return FirestoreDb.Create(projectId);
    }
}
