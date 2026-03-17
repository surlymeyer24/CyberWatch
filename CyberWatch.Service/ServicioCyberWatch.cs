using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;
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
    private readonly IFirebaseAlertService    _firebaseAlertas;
    private readonly AgentePipeServerService  _pipeServer;
    private readonly RastreadorProcesos       _rastreador;
    private readonly UmbralesSettings         _umbrales;
    private readonly ILogger<ServicioCyberWatch> _logger;

    public ServicioCyberWatch(
        MonitorActividadArchivos   monitor,
        IEvaluadorAmenazas         evaluador,
        IGestorAlertas             gestor,
        ILiquidadorProcesos        liquidador,
        IFirebaseAlertService      firebaseAlertas,
        AgentePipeServerService    pipeServer,
        RastreadorProcesos         rastreador,
        IOptions<UmbralesSettings> umbrales,
        ILogger<ServicioCyberWatch> logger)
    {
        _monitor         = monitor;
        _evaluador       = evaluador;
        _gestor          = gestor;
        _liquidador      = liquidador;
        _firebaseAlertas = firebaseAlertas;
        _pipeServer      = pipeServer;
        _rastreador      = rastreador;
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

                foreach (var nombreProceso in procesos)
                {
                    var reporte = _evaluador.Evaluar(snapshot, nombreProceso);
                    if (reporte != null)
                    {
                        _logger.LogWarning("[Servicio] AMENAZA DETECTADA: Proceso={Proceso} Escrituras={Esc} Renombrados={Ren} Extension={Ext} ExtDetectada={ExtDet}",
                            reporte.NombreProceso, reporte.EscriturasSospechosas, reporte.RenombradosSospechosas, reporte.ExtensionSospechosa, reporte.ExtensionDetectada ?? "N/A");
                        _gestor.Alertar(reporte);
                        await _firebaseAlertas.EnviarAlertaAsync(reporte, tokenCancelacion);
                        _liquidador.Liquidar(reporte);
                        await _pipeServer.NotificarAmenazaAsync(reporte.NombreProceso, tokenCancelacion);
                    }
                }

                _rastreador.LimpiarCache();

                await Task.Delay(TimeSpan.FromSeconds(_umbrales.IntervaloTiempoSeg), tokenCancelacion);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public override async Task StopAsync(CancellationToken tokenCancelacion)
    {
        _monitor.DetenerMonitorizacion();
        await base.StopAsync(tokenCancelacion);
    }
}
