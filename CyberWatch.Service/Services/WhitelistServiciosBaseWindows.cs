using System.Reflection;
using System.Text.Json;

namespace CyberWatch.Service.Services;

/// <summary>
/// Carga la whitelist de nombres cortos de servicio incluida como Embedded Resource (<c>Data/servicios_base_windows.json</c>).
/// </summary>
public static class WhitelistServiciosBaseWindows
{
    private const string SufijoRecurso = "servicios_base_windows.json";

    /// <summary>
    /// Deserializa el JSON embebido y devuelve un conjunto con comparación sin distinguir mayúsculas.
    /// </summary>
    public static HashSet<string> Cargar(Assembly? ensamblado = null)
    {
        ensamblado ??= typeof(WhitelistServiciosBaseWindows).Assembly;

        var nombreRecurso = ensamblado.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(SufijoRecurso, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"No se encontró recurso embebido que termine en '{SufijoRecurso}'.");

        using var stream = ensamblado.GetManifestResourceStream(nombreRecurso)
            ?? throw new InvalidOperationException($"No se pudo abrir el recurso '{nombreRecurso}'.");

        var lista = JsonSerializer.Deserialize<List<string>>(stream)
            ?? throw new InvalidOperationException("Whitelist de servicios: JSON vacío o inválido.");

        return new HashSet<string>(lista, StringComparer.OrdinalIgnoreCase);
    }
}
