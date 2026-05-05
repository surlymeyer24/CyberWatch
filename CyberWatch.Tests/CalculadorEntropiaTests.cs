using CyberWatch.Service.Detection;
using Xunit;

namespace CyberWatch.Tests;

public class CalculadorEntropiaTests
{
    [Fact]
    public void Entropia_texto_repetido_es_baja()
    {
        var data = "aaaaaaaa"u8.ToArray();
        var h = CalculadorEntropia.EntropiaBitsPorByte(data);
        Assert.True(h < 1.0, $"esperado bajo, obtuvo {h}");
    }

    [Fact]
    public void Entropia_bytes_uniformes_cerca_del_maximo()
    {
        var rnd = new Random(42);
        var buf = new byte[4096];
        rnd.NextBytes(buf);
        var h = CalculadorEntropia.EntropiaBitsPorByte(buf);
        Assert.True(h > 7.5, $"esperado alto, obtuvo {h}");
    }
}
