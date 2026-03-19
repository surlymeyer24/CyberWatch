using CyberWatch.Service.Config;
using CyberWatch.Service.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Response;

public class ServicioCuarentena : ICuarentena
{
    private readonly UmbralesSettings _umbrales;
    private readonly ILogger<ServicioCuarentena> _logger;

    public ServicioCuarentena(IOptions<UmbralesSettings> umbrales, ILogger<ServicioCuarentena> logger)
    {
        _umbrales = umbrales.Value;
        _logger = logger;
    }

    public ResultadoCuarentena Cuarentenar(ReporteAmenaza reporte)
    {
        var resultado = new ResultadoCuarentena { RutaOriginal = reporte.RutaEjecutable };

        if (string.IsNullOrEmpty(reporte.RutaEjecutable))
        {
            resultado.Error = "Ruta de ejecutable no disponible";
            _logger.LogWarning("[Cuarentena] {Error} para proceso {Proceso}", resultado.Error, reporte.NombreProceso);
            return resultado;
        }

        if (!File.Exists(reporte.RutaEjecutable))
        {
            resultado.Error = $"Archivo no encontrado: {reporte.RutaEjecutable}";
            _logger.LogWarning("[Cuarentena] {Error}", resultado.Error);
            return resultado;
        }

        if (EstaEnDirectorioProtegido(reporte.RutaEjecutable))
        {
            resultado.Error = $"Archivo en directorio protegido: {reporte.RutaEjecutable}";
            _logger.LogWarning("[Cuarentena] {Error}", resultado.Error);
            return resultado;
        }

        Directory.CreateDirectory(_umbrales.CarpetaCuarentena);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var nombreOriginal = Path.GetFileName(reporte.RutaEjecutable);
        var destino = Path.Combine(_umbrales.CarpetaCuarentena, $"{timestamp}_{nombreOriginal}.quarantine");

        try
        {
            File.Move(reporte.RutaEjecutable, destino);
        }
        catch (IOException)
        {
            Thread.Sleep(500);
            try
            {
                File.Move(reporte.RutaEjecutable, destino);
            }
            catch (Exception ex)
            {
                resultado.Error = $"No se pudo mover tras reintento: {ex.Message}";
                _logger.LogError(ex, "[Cuarentena] {Error}", resultado.Error);
                return resultado;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            resultado.Error = $"Acceso denegado: {ex.Message}";
            _logger.LogError(ex, "[Cuarentena] {Error}", resultado.Error);
            return resultado;
        }

        resultado.Exitosa = true;
        resultado.RutaCuarentena = destino;
        _logger.LogWarning("[Cuarentena] Archivo movido a cuarentena: {Origen} -> {Destino}",
            reporte.RutaEjecutable, destino);

        return resultado;
    }

    private bool EstaEnDirectorioProtegido(string ruta)
    {
        var rutaNorm = Path.GetFullPath(ruta);
        return _umbrales.DirectoriosProtegidos.Any(dir =>
            rutaNorm.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));
    }
}
