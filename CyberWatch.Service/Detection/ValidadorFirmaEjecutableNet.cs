using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CyberWatch.Service.Detection;

/// <summary>
/// Lee certificado Authenticode del PE con <see cref="X509Certificate2.CreateFromSignedFile(string)"/>
/// y comprueba la cadena con <see cref="X509Chain"/>.
/// </summary>
public sealed class ValidadorFirmaEjecutableNet : IValidadorFirmaEjecutable
{
    public ResultadoValidacionFirma Validar(string rutaEjecutableAbsoluta)
    {
        if (string.IsNullOrWhiteSpace(rutaEjecutableAbsoluta))
            return new ResultadoValidacionFirma(EstadoFirmaEjecutable.ArchivoInexistente, "Ruta vacía", null);

        try
        {
            if (!File.Exists(rutaEjecutableAbsoluta))
                return new ResultadoValidacionFirma(EstadoFirmaEjecutable.ArchivoInexistente, "Archivo no existe", null);

            X509Certificate2 cert;
            try
            {
                var embedded = X509Certificate.CreateFromSignedFile(rutaEjecutableAbsoluta);
                cert = new X509Certificate2(embedded);
            }
            catch (CryptographicException)
            {
                return new ResultadoValidacionFirma(EstadoFirmaEjecutable.SinFirma, "Sin firma Authenticode", null);
            }
            catch (PlatformNotSupportedException ex)
            {
                return new ResultadoValidacionFirma(EstadoFirmaEjecutable.Error, ex.Message, null);
            }

            using (cert)
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(15);

                var ok = chain.Build(cert);
                if (!ok)
                {
                    var detalle = string.Join(
                        "; ",
                        chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation.Trim()}"));
                    return new ResultadoValidacionFirma(
                        EstadoFirmaEjecutable.CadenaNoConfiable,
                        string.IsNullOrEmpty(detalle) ? "Cadena no confiable" : detalle,
                        cert.Subject);
                }

                return new ResultadoValidacionFirma(EstadoFirmaEjecutable.Confiable, null, cert.Subject);
            }
        }
        catch (Exception ex)
        {
            return new ResultadoValidacionFirma(EstadoFirmaEjecutable.Error, ex.Message, null);
        }
    }
}
