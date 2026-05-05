using System.ServiceProcess;
using CyberWatch.Service.Detection;
using CyberWatch.Service.Services;
using Xunit;

namespace CyberWatch.Tests;

/// <summary>
/// Pruebas pensadas para ejecutarse en una VM Windows (donde existen servicios SCM reales).
/// En Linux/macOS los métodos salen sin hacer aserciones fuertes (evita romper CI).
/// </summary>
public class ServiciosDesconocidosVmIntegrationTests
{
    private static List<ServicioDescriptor> EnumerarServiciosLocalesSinImagePathDetallado()
    {
        var lista = new List<ServicioDescriptor>();
        ServiceController[]? todos = null;
        try
        {
            todos = ServiceController.GetServices();
        }
        catch (PlatformNotSupportedException)
        {
            return lista;
        }

        foreach (var sc in todos)
        {
            try
            {
                lista.Add(new ServicioDescriptor(
                    sc.ServiceName,
                    sc.DisplayName ?? sc.ServiceName,
                    sc.Status.ToString(),
                    sc.StartType.ToString(),
                    ""));
            }
            finally
            {
                sc.Dispose();
            }
        }

        return lista;
    }

    [Fact]
    [Trait("Category", "VM")]
    public void VM01_EnWindows_HayServiciosInstalados()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var todos = EnumerarServiciosLocalesSinImagePathDetallado();
        Assert.True(todos.Count > 50, $"Se esperaban muchos servicios SCM en Windows; hay {todos.Count}.");
    }

    [Fact]
    [Trait("Category", "VM")]
    public void VM02_Analizador_ConServiciosReales_NoFallaYRespetaWhitelist()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var instalados = EnumerarServiciosLocalesSinImagePathDetallado();
        Assert.NotEmpty(instalados);

        var whitelist = WhitelistServiciosBaseWindows.Cargar();
        Assert.True(whitelist.Count >= 150);

        var exclusiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anterior = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var desconocidos = AnalizadorServicios.Evaluar(instalados, whitelist, exclusiones, anterior);

        Assert.All(desconocidos, d => Assert.DoesNotContain(d.Descriptor.Nombre, whitelist));
        foreach (var d in desconocidos)
            Assert.False(string.IsNullOrEmpty(d.Descriptor.Nombre));
    }

    [Fact]
    [Trait("Category", "VM")]
    public void VM03_WinDefend_EnWhitelist_NoSaleComoDesconocido()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var instalados = EnumerarServiciosLocalesSinImagePathDetallado();
        var soloDefender = instalados.Where(s =>
                string.Equals(s.Nombre, "WinDefend", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (soloDefender.Count == 0)
        {
            return;
        }

        var whitelist = WhitelistServiciosBaseWindows.Cargar();
        Assert.Contains("WinDefend", whitelist);

        var r = AnalizadorServicios.Evaluar(
            soloDefender,
            whitelist,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(r);
    }

    [Fact]
    [Trait("Category", "VM")]
    public void VM04_NormalizarImagePath_IgualQueElServicio()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(
            @"C:\Windows\System32\svchost.exe",
            ServiciosDesconocidosService.NormalizarImagePath(
                @"""C:\Windows\System32\svchost.exe"" -k netsvcs"));

        var esperadoProgramFiles = Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\App\svc.exe");
        Assert.Equal(
            esperadoProgramFiles,
            ServiciosDesconocidosService.NormalizarImagePath(
                @"%ProgramFiles%\App\svc.exe"));

        Assert.Equal("", ServiciosDesconocidosService.NormalizarImagePath(null));
    }

    [Fact]
    [Trait("Category", "VM")]
    public void VM05_IdDocumentoFirestore_EscapaSegmentosReservados()
    {
        Assert.Equal("foo_bar", ServiciosDesconocidosService.IdDocumentoFirestore(@"foo/bar"));
        Assert.Equal("_..", ServiciosDesconocidosService.IdDocumentoFirestore(".."));
    }
}
