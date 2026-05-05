using System.Collections.Concurrent;
using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Service.Models;
using CyberWatch.Service.Monitoring;
using CyberWatch.Service.Response;
using CyberWatch.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service;

public class ServicioCyberWatch : BackgroundService
{
    private readonly MonitorActividadArchivos _monitor;
    private readonly IEvaluadorAmenazas       _evaluador;
    private readonly IGestorAlertas           _gestor;
    private readonly ILiquidadorProcesos      _liquidador;
    private readonly ICuarentena             _cuarentena;
    private readonly IFirebaseAlertService    _firebaseAlertas;
    private readonly AgentePipeServerService  _pipeServer;
    private readonly UmbralesSettings         _umbrales;
    private readonly ILogger<ServicioCyberWatch> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _amenazasPrevias = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _cooldownLiquidacion = new(StringComparer.OrdinalIgnoreCase);

    public ServicioCyberWatch(
        MonitorActividadArchivos   monitor,
        IEvaluadorAmenazas         evaluador,
        IGestorAlertas             gestor,
        ILiquidadorProcesos        liquidador,
        ICuarentena                cuarentena,
        IFirebaseAlertService      firebaseAlertas,
        AgentePipeServerService    pipeServer,
        IOptions<UmbralesSettings> umbrales,
        ILogger<ServicioCyberWatch> logger)
    {
        _monitor         = monitor;
        _evaluador       = evaluador;
        _gestor          = gestor;
        _liquidador      = liquidador;
        _cuarentena      = cuarentena;
        _firebaseAlertas = firebaseAlertas;
        _pipeServer      = pipeServer;
        _umbrales        = umbrales.Value;
        _logger          = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken tokenCancelacion)
    {
        _logger.LogInformation("[Servicio] Iniciando monitorización...");
        _monitor.IniciarMonitorizacion();
        _logger.LogInformation("[Servicio] Monitorización activa. Ciclo cada {Seg}s", _umbrales.IntervaloTiempoSeg);

        while (!tokenCancelacion.IsCancellationRequested)
        {
            try
            {
                var snapshot = _monitor.TomarSnapshotYLimpiar();
                var procesos = snapshot.Select(e => e.NombreProceso).Distinct().ToList();

                if (snapshot.Count > 0)
                    _logger.LogInformation("[Servicio] Ciclo: {Total} eventos, {Procesos} procesos: [{Lista}]",
                        snapshot.Count, procesos.Count, string.Join(", ", procesos));

                // Fase 1: recolectar todas las amenazas del ciclo
                var amenazas = new List<(ReporteAmenaza reporte, int eventosEnCiclo)>();
                foreach (var nombreProceso in procesos)
                {
                    var reporte = _evaluador.Evaluar(snapshot, nombreProceso);
                    if (reporte != null)
                    {
                        var eventosDelProceso = snapshot
                            .Where(e => string.Equals(e.NombreProceso, nombreProceso, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        reporte.RutaEjecutable = eventosDelProceso
                            .Select(e => e.RutaEjecutable)
                            .FirstOrDefault(r => !string.IsNullOrEmpty(r));

                        amenazas.Add((reporte, eventosDelProceso.Count));
                    }
                }

                // Fase 2: protección contra falsos positivos masivos
                bool suspenderRespuesta = amenazas.Count > _umbrales.MaxAmenazasPorCiclo;
                if (suspenderRespuesta)
                    _logger.LogWarning("[Servicio] PROTECCION: {Count} amenazas en un ciclo (max={Max}). Posibles falsos positivos — solo registro, sin respuesta activa.",
                        amenazas.Count, _umbrales.MaxAmenazasPorCiclo);

                // Fase 3: procesar cada amenaza
                foreach (var (reporte, eventosProceso) in amenazas)
                {
                    _logger.LogWarning("[Servicio] AMENAZA DETECTADA: Proceso={Proceso} Puntuacion={Punt} Escrituras={Esc} Renombrados={Ren} Extension={Ext} ExtDetectada={ExtDet} Ruta={Ruta}",
                        reporte.NombreProceso, reporte.Puntuacion, reporte.EscriturasSospechosas, reporte.RenombradosSospechosas,
                        reporte.ExtensionSospechosa, reporte.ExtensionDetectada ?? "N/A", reporte.RutaEjecutable ?? "N/A");

                    ResultadoCuarentena resultadoCuarentena;
                    bool respuestaActiva = false;

                    // Decidir si se debe responder activamente (kill + cuarentena)
                    if (suspenderRespuesta)
                    {
                        resultadoCuarentena = new ResultadoCuarentena
                        {
                            RutaOriginal = reporte.RutaEjecutable,
                            Error = $"Respuesta suspendida: {amenazas.Count} amenazas en el ciclo superan el limite de {_umbrales.MaxAmenazasPorCiclo}"
                        };
                    }
                    else if (EsProcesoProtegido(reporte.RutaEjecutable))
                    {
                        _logger.LogWarning("[Servicio] PROTECCION: No se liquida {Proceso} — ruta en directorio protegido: {Ruta}",
                            reporte.NombreProceso, reporte.RutaEjecutable);
                        resultadoCuarentena = new ResultadoCuarentena
                        {
                            RutaOriginal = reporte.RutaEjecutable,
                            Error = $"Proceso protegido: ruta en directorio del sistema"
                        };
                    }
                    else if (EnCooldownLiquidacion(reporte.NombreProceso))
                    {
                        _logger.LogInformation("[Servicio] COOLDOWN: {Proceso} fue liquidado recientemente, omitiendo respuesta activa.",
                            reporte.NombreProceso);
                        resultadoCuarentena = new ResultadoCuarentena
                        {
                            RutaOriginal = reporte.RutaEjecutable,
                            Error = $"Cooldown activo: proceso liquidado hace menos de {_umbrales.CooldownLiquidacionMinutos} minutos"
                        };
                    }
                    else
                    {
                        // Respuesta activa: kill + posible cuarentena
                        respuestaActiva = true;
                        _gestor.Alertar(reporte);
                        _liquidador.Liquidar(reporte);
                        _cooldownLiquidacion[reporte.NombreProceso] = DateTime.Now;

                        // Cuarentena solo si reincide en 5 minutos
                        if (_amenazasPrevias.TryGetValue(reporte.NombreProceso, out var primeraDeteccion)
                            && (DateTime.Now - primeraDeteccion).TotalMinutes < 5)
                        {
                            resultadoCuarentena = _cuarentena.Cuarentenar(reporte);
                            _amenazasPrevias.TryRemove(reporte.NombreProceso, out _);
                            if (!resultadoCuarentena.Exitosa)
                                _logger.LogWarning("[Servicio] Cuarentena fallida para {Proceso}: {Error}",
                                    reporte.NombreProceso, resultadoCuarentena.Error);
                        }
                        else
                        {
                            _amenazasPrevias[reporte.NombreProceso] = DateTime.Now;
                            resultadoCuarentena = new ResultadoCuarentena
                            {
                                RutaOriginal = reporte.RutaEjecutable,
                                Error = "Primera deteccion: solo liquidacion, sin cuarentena"
                            };
                            _logger.LogWarning("[Servicio] Primera deteccion de {Proceso}: liquidado sin cuarentena. Si reincide en 5 min se aplicara cuarentena.",
                                reporte.NombreProceso);
                        }
                    }

                    await _firebaseAlertas.EnviarAlertaAsync(reporte, resultadoCuarentena, eventosProceso, tokenCancelacion);
                    if (respuestaActiva)
                        await _pipeServer.NotificarAmenazaAsync(reporte.NombreProceso, tokenCancelacion);
                }

                await Task.Delay(TimeSpan.FromSeconds(_umbrales.IntervaloTiempoSeg), tokenCancelacion);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private bool EsProcesoProtegido(string? rutaEjecutable)
    {
        if (string.IsNullOrEmpty(rutaEjecutable))
            return false;
        return _umbrales.DirectoriosProtegidos.Any(dir =>
            rutaEjecutable.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
    }

    private bool EnCooldownLiquidacion(string nombreProceso)
    {
        if (_cooldownLiquidacion.TryGetValue(nombreProceso, out var ultimaLiquidacion))
            return (DateTime.Now - ultimaLiquidacion).TotalMinutes < _umbrales.CooldownLiquidacionMinutos;
        return false;
    }

    public override async Task StopAsync(CancellationToken tokenCancelacion)
    {
        _monitor.DetenerMonitorizacion();
        await base.StopAsync(tokenCancelacion);
    }
}
