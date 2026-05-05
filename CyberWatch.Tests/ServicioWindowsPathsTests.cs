using CyberWatch.Service.Detection;
using Xunit;

namespace CyberWatch.Tests;

public class ServicioWindowsPathsTests
{
    [Fact]
    public void ExtraerRutaBinarioFirma_exe_con_argumentos()
    {
        var r = ServicioWindowsPaths.ExtraerRutaBinarioFirma(@"C:\Windows\System32\svchost.exe -k netsvcs");
        Assert.Equal(@"C:\Windows\System32\svchost.exe", r);
    }

    [Fact]
    public void ExtraerRutaBinarioFirma_sys()
    {
        var r = ServicioWindowsPaths.ExtraerRutaBinarioFirma(@"\??\C:\Drivers\x.sys something");
        Assert.EndsWith(".sys", r, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizarPathParaFileSystem_quita_prefijo_nt()
    {
        var r = ServicioWindowsPaths.NormalizarPathParaFileSystem(@"\??\C:\Temp\a.exe");
        Assert.Equal(@"C:\Temp\a.exe", r);
    }
}
