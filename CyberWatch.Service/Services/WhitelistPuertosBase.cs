using System.Reflection;
using System.Text.Json;

namespace CyberWatch.Service.Services;

/// <summary>
/// Whitelist y lista negra de puertos embebidas como JSON (<c>Data/puertos_base_windows.json</c>,
/// <c>Data/puertos_sospechosos.json</c>).
/// </summary>
public static class WhitelistPuertosBase
{
    private const string SufijoBase = "puertos_base_windows.json";
    private const string SufijoSospechosos = "puertos_sospechosos.json";

    public static HashSet<int> CargarPuertosBase(Assembly? ensamblado = null)
        => CargarEnteros(SufijoBase, ensamblado);

    public static HashSet<int> CargarPuertosSospechosos(Assembly? ensamblado = null)
        => CargarEnteros(SufijoSospechosos, ensamblado);

    private static HashSet<int> CargarEnteros(string sufijo, Assembly? ensamblado)
    {
        ensamblado ??= typeof(WhitelistPuertosBase).Assembly;

        var nombreRecurso = ensamblado.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(sufijo, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"No se encontró recurso embebido que termine en '{sufijo}'.");

        using var stream = ensamblado.GetManifestResourceStream(nombreRecurso)
            ?? throw new InvalidOperationException($"No se pudo abrir el recurso '{nombreRecurso}'.");

        var lista = JsonSerializer.Deserialize<List<int>>(stream)
            ?? throw new InvalidOperationException($"Lista de puertos '{sufijo}' vacía o inválida.");

        return lista.ToHashSet();
    }
}
