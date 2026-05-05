namespace CyberWatch.Service.Detection;

/// <summary>Resultado de validar Authenticode en un archivo PE.</summary>
public enum EstadoFirmaEjecutable
{
    Confiable,
    SinFirma,
    CadenaNoConfiable,
    ArchivoInexistente,
    Error
}

/// <summary>Salida de <see cref="IValidadorFirmaEjecutable.Validar"/>.</summary>
public sealed record ResultadoValidacionFirma(
    EstadoFirmaEjecutable Estado,
    string? Mensaje,
    string? SubjectCertificado);

/// <summary>Validación MVP con cadena X.509 del SO (evolución posible: WinVerifyTrust).</summary>
public interface IValidadorFirmaEjecutable
{
    ResultadoValidacionFirma Validar(string rutaEjecutableAbsoluta);
}
