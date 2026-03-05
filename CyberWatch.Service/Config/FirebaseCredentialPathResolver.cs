using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Config;

/// <summary>
/// Resuelve la ruta del JSON de cuenta de servicio de Firebase si no está configurada o el archivo no existe.
/// Busca en auth/serviceAccountKey.json respecto al directorio actual o a la raíz del repo.
/// </summary>
public class FirebaseCredentialPathResolver : IPostConfigureOptions<FirebaseSettings>
{
    public void PostConfigure(string? name, FirebaseSettings options)
    {
        if (options == null) return;

        // Si ya hay ruta y el archivo existe, normalizar a ruta absoluta para evitar problemas de directorio actual
        if (!string.IsNullOrWhiteSpace(options.CredentialPath))
        {
            var path = options.CredentialPath.Trim();
            var full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
            if (File.Exists(full))
            {
                options.CredentialPath = full;
                return;
            }
        }

        // Buscar auth/serviceAccountKey.json en ubicaciones habituales
        var currentDir = Directory.GetCurrentDirectory();
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        var candidates = new List<string>
        {
            Path.Combine(currentDir, "auth", "serviceAccountKey.json"),
            Path.Combine(currentDir, "..", "auth", "serviceAccountKey.json"),
            Path.Combine(currentDir, "..", "..", "auth", "serviceAccountKey.json")
        };

        // Subir desde bin/Debug/net8.0 hasta encontrar auth/serviceAccountKey.json (raíz del repo)
        var dir = baseDir;
        for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "auth", "serviceAccountKey.json");
            if (!candidates.Contains(candidate))
                candidates.Add(candidate);
            dir = Path.GetDirectoryName(dir);
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    options.CredentialPath = full;
                    return;
                }
            }
            catch
            {
                // ignorar rutas inválidas
            }
        }
    }
}
