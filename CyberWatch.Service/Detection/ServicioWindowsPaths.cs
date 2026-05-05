namespace CyberWatch.Service.Detection;

/// <summary>
/// Normalización de ImagePath del registro SCM y extracción del binario para verificación Authenticode.
/// </summary>
public static class ServicioWindowsPaths
{
    /// <summary>Expande variables de entorno y normaliza comillas típicas de ImagePath (misma lógica histórica del monitor de servicios).</summary>
    public static string NormalizarImagePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var s = Environment.ExpandEnvironmentVariables(raw.Trim());

        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1].Trim();

        if (s.StartsWith('"'))
        {
            var cierre = s.IndexOf('"', 1);
            if (cierre > 1)
                return s[1..cierre].Trim();
        }

        return s.Trim();
    }

    /// <summary>
    /// Tras <see cref="NormalizarImagePath"/>, obtiene solo la ruta del PE (.exe, .dll, .sys) antes de argumentos en línea.
    /// </summary>
    public static string ExtraerRutaBinarioFirma(string expandedImagePath)
    {
        if (string.IsNullOrWhiteSpace(expandedImagePath))
            return "";

        var s = expandedImagePath.Trim();

        foreach (var ext in new[] { ".exe", ".dll", ".sys" })
        {
            var i = s.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
                return s[..(i + ext.Length)];
        }

        var space = s.IndexOf(' ');
        return space > 0 ? s[..space].Trim() : s;
    }

    /// <summary>Quita prefijos tipo NT kernel (<c>\??\</c>) para acceso con API de archivos.</summary>
    public static string NormalizarPathParaFileSystem(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }
}
