namespace CyberWatch.Service.Detection;

/// <summary>
/// Lógica pura: whitelist base, exclusiones configurables y detección de “nuevo” respecto al ciclo anterior.
/// </summary>
public static class AnalizadorServicios
{
    /// <summary>
    /// Devuelve los servicios que no están en la whitelist ni en exclusiones, con <see cref="ServicioDesconocidoEvaluado.EsNuevo"/>
    /// si el nombre no estaba en <paramref name="desconocidosCicloAnterior"/>.
    /// </summary>
    public static IReadOnlyList<ServicioDesconocidoEvaluado> Evaluar(
        IReadOnlyList<ServicioDescriptor> instalados,
        HashSet<string> whitelistBase,
        HashSet<string> exclusionesConfig,
        HashSet<string> desconocidosCicloAnterior)
    {
        ArgumentNullException.ThrowIfNull(instalados);
        ArgumentNullException.ThrowIfNull(whitelistBase);
        ArgumentNullException.ThrowIfNull(exclusionesConfig);
        ArgumentNullException.ThrowIfNull(desconocidosCicloAnterior);

        var exclusiones = new HashSet<string>(exclusionesConfig, StringComparer.OrdinalIgnoreCase);

        var resultado = new List<ServicioDesconocidoEvaluado>();
        foreach (var s in instalados)
        {
            if (whitelistBase.Contains(s.Nombre))
                continue;
            if (exclusiones.Contains(s.Nombre))
                continue;

            var esNuevo = !desconocidosCicloAnterior.Contains(s.Nombre);
            resultado.Add(new ServicioDesconocidoEvaluado(s, esNuevo));
        }

        return resultado;
    }
}

/// <summary>Resultado por servicio clasificado como no-base.</summary>
public sealed record ServicioDesconocidoEvaluado(ServicioDescriptor Descriptor, bool EsNuevo);
