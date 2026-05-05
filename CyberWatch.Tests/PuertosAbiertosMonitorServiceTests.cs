using CyberWatch.Service.Detection;
using CyberWatch.Service.Services;
using Xunit;

namespace CyberWatch.Tests;

public class PuertosAbiertosMonitorServiceTests
{
    [Fact]
    public void IdDocumentoFirestore_Sustituye_caracteres_problematicos_en_ips_y_paths()
    {
        var d = new PuertoTcpDescriptor
        {
            EstadoTexto = "Listen",
            EstadoRaw = 2,
            IpLocal = "192.168.1.10",
            PuertoLocal = 4444,
            IpRemota = "0.0.0.0",
            PuertoRemoto = 0,
            Pid = 1234,
            NombreProceso = "foo",
            RutaProceso = @"C:\Temp\x.exe"
        };

        var id = PuertosAbiertosMonitorService.IdDocumentoFirestore(d);

        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain('\\', id);
        Assert.Contains("4444", id);
        Assert.Contains("1234", id);
    }
}
