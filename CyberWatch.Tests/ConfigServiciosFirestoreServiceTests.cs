using CyberWatch.Service.Services;
using Xunit;

namespace CyberWatch.Tests;

public class ConfigServiciosFirestoreServiceTests
{
    [Fact]
    public void NormalizarHashHex_AceptaHex64MayusculasYEspacios()
    {
        const string esperado = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var raw = " E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855 ";
        Assert.Equal(esperado, ConfigServiciosFirestoreService.NormalizarHashHex(raw));
    }

    [Fact]
    public void NormalizarHashHex_RechazaLongitudIncorrecta()
    {
        Assert.Equal("", ConfigServiciosFirestoreService.NormalizarHashHex("abc"));
    }
}
