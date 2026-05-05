using CyberWatch.Service.Config;
using CyberWatch.Service.Models;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Detection;

public class EvaluadorAmenazas : IEvaluadorAmenazas
{
    private readonly UmbralesSettings _umbrales;
    private readonly ICalculadorEntropia _entropia;

    public EvaluadorAmenazas(IOptions<UmbralesSettings> umbrales, ICalculadorEntropia entropia)
    {
        _umbrales = umbrales.Value;
        _entropia = entropia;
    }

    public ReporteAmenaza? Evaluar(List<EventoArchivo> eventos, string nombreProceso)
    {
        if (string.IsNullOrWhiteSpace(nombreProceso))
            return null;

        if (EstaExcluido(nombreProceso))
            return null;

        bool escriturasSospechosas = SuperaUmbralEscritura(eventos, nombreProceso);
        bool renombradosSospechosas = SuperaUmbralRenombrados(eventos, nombreProceso);
        bool extensionSospechosa = eventos.Any(TieneExtensionSospechosa);

        int puntuacion = 0;

        if (extensionSospechosa)
            puntuacion += 3;

        if (escriturasSospechosas && renombradosSospechosas)
            puntuacion += 3;
        else if (escriturasSospechosas)
            puntuacion += 1;
        else if (renombradosSospechosas)
            puntuacion += 1;

        var puntuacionBase = puntuacion;
        double? entVal = null;
        var entAplicada = false;
        string? rutaMuestra = null;

        if (_umbrales.EntropiaHabilitada
            && puntuacionBase >= _umbrales.EntropiaMinimoPuntuacionBase
            && puntuacionBase < _umbrales.UmbralPuntuacionAmenaza
            && puntuacionBase > 0)
        {
            var patron = !_umbrales.EntropiaRequierePatronRansomware
                         || escriturasSospechosas
                         || renombradosSospechosas
                         || extensionSospechosa;

            if (patron)
            {
                rutaMuestra = ElegirRutaParaMuestraEntropia(eventos, nombreProceso);
                if (!string.IsNullOrEmpty(rutaMuestra))
                {
                    entVal = _entropia.CalcularEntropiaShannonMuestra(
                        rutaMuestra,
                        _umbrales.EntropiaTamanoMuestraKb);

                    if (entVal.HasValue && entVal.Value >= _umbrales.EntropiaUmbralAlto)
                    {
                        puntuacion += _umbrales.EntropiaBonusPuntos;
                        entAplicada = true;
                    }
                }
            }
        }

        if (puntuacion >= _umbrales.UmbralPuntuacionAmenaza)
        {
            var extDetectada = eventos
                .Where(e => e.TipoEvento == TipoEvento.ExtensionSospechosa)
                .Select(e => Path.GetExtension(e.RutaArchivo).ToLowerInvariant())
                .FirstOrDefault();
            return new ReporteAmenaza(nombreProceso, escriturasSospechosas, renombradosSospechosas, extensionSospechosa, extDetectada)
            {
                Puntuacion = puntuacion,
                EntropiaMuestra = entVal,
                EntropiaAplicadaComoBonus = entAplicada,
                RutaArchivoMuestraEntropia = rutaMuestra
            };
        }

        return null;
    }

    private string? ElegirRutaParaMuestraEntropia(List<EventoArchivo> eventos, string nombreProceso)
    {
        bool MismoProceso(EventoArchivo e) =>
            string.Equals(e.NombreProceso, nombreProceso, StringComparison.OrdinalIgnoreCase);

        IEnumerable<EventoArchivo> baseq = eventos.Where(MismoProceso);

        string? PrimeraRutaPreferida(IEnumerable<EventoArchivo> q) =>
            q.Where(e => e.TipoEvento is TipoEvento.Escritura or TipoEvento.Creacion)
                .Where(e => !EsExtensionEntropiaAltaEsperada(e.RutaArchivo))
                .OrderByDescending(e => e.FechaHora)
                .Select(e => e.RutaArchivo)
                .FirstOrDefault(r => !string.IsNullOrEmpty(r));

        var ruta = PrimeraRutaPreferida(baseq);
        if (!string.IsNullOrEmpty(ruta))
            return ruta;

        ruta = baseq
            .Where(e => e.TipoEvento is TipoEvento.Escritura or TipoEvento.Creacion)
            .OrderByDescending(e => e.FechaHora)
            .Select(e => e.RutaArchivo)
            .FirstOrDefault(rr => !string.IsNullOrEmpty(rr));
        if (!string.IsNullOrEmpty(ruta))
            return ruta;

        ruta = baseq
            .Where(e => e.TipoEvento == TipoEvento.Renombrado)
            .Where(e => !EsExtensionEntropiaAltaEsperada(e.RutaArchivo))
            .OrderByDescending(e => e.FechaHora)
            .Select(e => e.RutaArchivo)
            .FirstOrDefault(rr => !string.IsNullOrEmpty(rr));

        return string.IsNullOrEmpty(ruta)
            ? baseq.OrderByDescending(e => e.FechaHora).Select(e => e.RutaArchivo).FirstOrDefault(rr => !string.IsNullOrEmpty(rr))
            : ruta;
    }

    private bool EsExtensionEntropiaAltaEsperada(string rutaArchivo)
    {
        var ext = Path.GetExtension(rutaArchivo).ToLowerInvariant();
        return _umbrales.ExtensionesEntropiaAltaEsperada.Contains(ext);
    }

    private bool EstaExcluido(string nombreProceso)
    {
        var nombre = nombreProceso.Trim();
        if (nombre.Length == 0) return true;
        var sinExt = nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? nombre[..^4]
            : nombre;
        return _umbrales.ProcesosExcluidos.Any(excluido =>
            string.Equals(excluido.Trim(), nombre, StringComparison.OrdinalIgnoreCase)
            || string.Equals(excluido.Trim(), sinExt, StringComparison.OrdinalIgnoreCase));
    }

    public bool TieneExtensionSospechosa(EventoArchivo evento)
    {
        var extension = Path.GetExtension(evento.RutaArchivo).ToLowerInvariant();
        return _umbrales.ExtensionesSospechosas.Contains(extension);
    }

    private bool SuperaUmbralEscritura(List<EventoArchivo> eventos, string nombreProceso)
    {
        var count = eventos.Count(e => string.Equals(e.NombreProceso, nombreProceso, StringComparison.OrdinalIgnoreCase));
        return count >= _umbrales.MaxEscrituraPermitida;
    }

    private bool SuperaUmbralRenombrados(List<EventoArchivo> eventos, string nombreProceso)
    {
        var count = eventos.Count(e => string.Equals(e.NombreProceso, nombreProceso, StringComparison.OrdinalIgnoreCase)
                                     && e.TipoEvento == TipoEvento.Renombrado);
        return count >= _umbrales.MaxRenombradosPermitidos;
    }
}
