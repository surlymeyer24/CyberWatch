using CyberWatch.Service.Detection;
using Xunit;

namespace CyberWatch.Tests;

/// <summary>
/// TAREA 5: casos mínimos sobre la lógica pura <see cref="AnalizadorServicios"/>.
/// </summary>
public class AnalizadorServiciosTests
{
    private static ServicioDescriptor Desc(string nombre) =>
        new(nombre, nombre + " display", "Running", "Automatic", @"C:\Apps\svc.exe");

    [Fact]
    public void EnWhitelist_NoApareceComoDesconocido()
    {
        var instalados = new List<ServicioDescriptor> { Desc("WinDefend") };
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WinDefend" };
        var exclusiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anterior = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var r = AnalizadorServicios.Evaluar(instalados, whitelist, exclusiones, anterior);

        Assert.Empty(r);
    }

    [Fact]
    public void FueraDeWhitelist_ApareceComoDesconocido()
    {
        var instalados = new List<ServicioDescriptor> { Desc("MiAgenteInterno") };
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WinDefend" };
        var exclusiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anterior = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var r = AnalizadorServicios.Evaluar(instalados, whitelist, exclusiones, anterior);

        Assert.Single(r);
        Assert.Equal("MiAgenteInterno", r[0].Descriptor.Nombre);
    }

    [Fact]
    public void FueraDeWhitelist_Y_NoEstabaEnCicloAnterior_EsNuevo()
    {
        var instalados = new List<ServicioDescriptor> { Desc("NuevoSvc") };
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exclusiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anterior = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var r = AnalizadorServicios.Evaluar(instalados, whitelist, exclusiones, anterior);

        Assert.Single(r);
        Assert.True(r[0].EsNuevo);
    }

    [Fact]
    public void FueraDeWhitelist_PeroYaEstabaEnCicloAnterior_NoEsNuevo()
    {
        var instalados = new List<ServicioDescriptor> { Desc("PersistenteSvc") };
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exclusiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anterior = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PersistenteSvc" };

        var r = AnalizadorServicios.Evaluar(instalados, whitelist, exclusiones, anterior);

        Assert.Single(r);
        Assert.False(r[0].EsNuevo);
    }

    [Fact]
    public void FueraDeWhitelist_PeroEnExclusionesConfig_NoEsDesconocido()
    {
        var instalados = new List<ServicioDescriptor> { Desc("CyberWatchVendor") };
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WinDefend" };
        var exclusiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CyberWatchVendor" };
        var anterior = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var r = AnalizadorServicios.Evaluar(instalados, whitelist, exclusiones, anterior);

        Assert.Empty(r);
    }
}
