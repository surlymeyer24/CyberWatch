using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Service.Monitoring;
using CyberWatch.Service.Response;
using CyberWatch.Service.Services;
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

    public ServicioCyberWatch(
        MonitorActividadArchivos   monitor,
        IEvaluadorAmenazas         evaluador,
        IGestorAlertas             gestor,
        ILiquidadorProcesos        liquidador,
        IFirebaseAlertService      firebaseAlertas,
        AgentePipeServerService    pipeServer,
        RastreadorProcesos         rastreador,
        IOptions<UmbralesSettings> umbrales)
    {
        _monitor         = monitor;
        _evaluador       = evaluador;
        _gestor          = gestor;
        _liquidador      = liquidador;
        _firebaseAlertas = firebaseAlertas;
        _pipeServer      = pipeServer;
        _rastreador      = rastreador;
        _umbrales        = umbrales.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken tokenCancelacion)
    {
        _monitor.IniciarMonitorizacion();

        while (!tokenCancelacion.IsCancellationRequested)
        {
            try
            {
                var snapshot = _monitor.Eventos.ToList();
                foreach (var nombreProceso in snapshot.Select(e => e.NombreProceso).Distinct())
                {
                    var reporte = _evaluador.Evaluar(snapshot, nombreProceso);
                    if (reporte != null)
                    {
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
