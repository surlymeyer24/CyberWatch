namespace CyberWatch.Service.Detection;

/// <summary>
/// Entropía de Shannon sobre una muestra de bytes (0–8 bits por byte para distribución uniforme por símbolo).
/// </summary>
public interface ICalculadorEntropia
{
    /// <summary>
    /// Lee hasta <paramref name="tamanoMuestraKb"/> KB del archivo y devuelve entropía o null si no se puede leer.
    /// </summary>
    double? CalcularEntropiaShannonMuestra(string rutaArchivo, int tamanoMuestraKb);
}

public sealed class CalculadorEntropia : ICalculadorEntropia
{
    public double? CalcularEntropiaShannonMuestra(string rutaArchivo, int tamanoMuestraKb)
    {
        if (string.IsNullOrWhiteSpace(rutaArchivo) || tamanoMuestraKb <= 0)
            return null;

        var maxBytes = Math.Min(tamanoMuestraKb, 8192) * 1024L; // tope duro 8 MB
        try
        {
            using var fs = new FileStream(
                rutaArchivo,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (fs.Length == 0)
                return 0;

            var len = (int)Math.Min(maxBytes, fs.Length);
            var buffer = len <= 8 * 1024 * 1024 ? new byte[len] : Array.Empty<byte>();
            if (buffer.Length == 0)
                return null;

            var read = fs.Read(buffer, 0, len);
            if (read <= 0)
                return null;

            return EntropiaBitsPorByte(buffer.AsSpan(0, read));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Entropía en bits por byte (máx. ~8 para bytes uniformes).</summary>
    internal static double EntropiaBitsPorByte(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return 0;

        Span<int> counts = stackalloc int[256];
        foreach (var b in data)
            counts[b]++;

        double h = 0;
        double n = data.Length;
        for (var i = 0; i < 256; i++)
        {
            var c = counts[i];
            if (c == 0) continue;
            var p = c / n;
            h -= p * Math.Log2(p);
        }

        return h;
    }
}
