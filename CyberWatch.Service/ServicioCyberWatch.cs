using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Service.Monitoring;
using CyberWatch.Service.Response;
using CyberWatch.Service.Services;

namespace CyberWatch.Service
{
    public class ServicioCyberWatch : BackgroundService
    {
        private readonly MonitorActividadArchivos _monitor;
        private readonly IFirebaseAlertService _firebaseAlertas;

        public ServicioCyberWatch(IFirebaseAlertService firebaseAlertas)
        {
            _monitor = new MonitorActividadArchivos();
            _firebaseAlertas = firebaseAlertas;
        }

        protected override async Task ExecuteAsync(CancellationToken tokenCancelacion)
        {
            _monitor.IniciarMonitorizacion();

            while (!tokenCancelacion.IsCancellationRequested)
            {
                foreach (var nombreProceso in _monitor.Eventos.Select(e => e.NombreProceso).Distinct())
                {
                    var reporte = EvaluadorAmenazas.Evaluar(_monitor.Eventos, nombreProceso);
                    if (reporte != null)
                    {
                        GestorAlertas.Alertar(reporte);
                        await _firebaseAlertas.EnviarAlertaAsync(reporte, tokenCancelacion);
                        LiquidarProcesos.Liquidar(reporte);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(ConfiguracionUmbrales.IntervaloTiempoSeg), tokenCancelacion);
            }
        }

        public override async Task StopAsync(CancellationToken tokenCancelacion)
        {
            _monitor.DetenerMonitorizacion();
            await base.StopAsync(tokenCancelacion);
        }
    }
}