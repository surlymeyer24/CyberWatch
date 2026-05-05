using CyberWatch.Service.Services;
using Xunit;

namespace CyberWatch.Tests;

/// <summary>
/// TAREA 1: valida que la whitelist embebida existe y cumple el mínimo acordado.
/// </summary>
public class ServiciosBaseWindowsResourceTests
{
    [Fact]
    public void ServiciosBaseWindows_EmbeddedResource_Existe_Y_TieneAlMenos150Servicios()
    {
        var conjunto = WhitelistServiciosBaseWindows.Cargar();

        Assert.True(conjunto.Count >= 150, $"Se esperaban al menos 150 nombres de servicio; hay {conjunto.Count}.");
        Assert.Contains("WinDefend", conjunto);
        Assert.Contains("wuauserv", conjunto);
    }
}
