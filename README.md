# CyberWatch

Servicio Windows de detección de ransomware con sincronización en tiempo real a Firebase Firestore.
Desarrollado en C# / .NET 8.0.

---

## Arquitectura

CyberWatch se compone de dos procesos que corren en paralelo:

| Componente | Sesión | Privilegio | Rol |
|---|---|---|---|
| **CyberWatch.Service** | Session 0 | SYSTEM | Detección de amenazas, sync Firestore, geolocalización IP, servidor Named Pipe |
| **CyberWatch.UserAgent** | Sesión de usuario | Usuario normal | GPS (Windows Location API), capturas de pantalla, historial de navegación, cliente Named Pipe, comandos remotos |

El UserAgent no tiene ventana visible (`WinExe`) y es lanzado automáticamente por el Programador de tareas al iniciar sesión (la tarea es registrada por el Service al arrancar).

### Flujo de comunicación

```
Dashboard (React, `Front/`) ──── Firestore ───► Service (Session 0)
                                        │
                                   Named Pipe (CyberWatch_AgentPipe)
                                        │
                                        ▼
                                   UserAgent (sesión usuario)
                                        │
                                   Firestore (GPS, capturas, comandos)
```

---

## Proyectos (.csproj)

| Proyecto | Tipo | Rol |
|---|---|---|
| `CyberWatch.Service` | Worker Service | Servicio Windows principal (Session 0) |
| `CyberWatch.UserAgent` | WinExe | Agente en sesión de usuario (invisible) |
| `CyberWatch.Shared` | Library | Modelos, config y logging compartidos |
| `Front` | React (Vite + TypeScript) | Dashboard de monitoreo (web), conectado a Firestore en tiempo real |
| `CyberWatch.DumpFirestore` | Console | Utilidad de backup/debug de Firestore |
| `CyberWatch.Tests` | xUnit | Pruebas unitarias |

---

## Firestore

### Colecciones

| Colección | Contenido |
|---|---|
| `cyberwatch_instancias/{machineId}` | Registro de cada máquina: hostname, IP, versión, geolocalización IP y GPS, última conexión, comando remoto |
| `cyberwatch_instancias/{machineId}/alertas/` | Subcollection de alertas por máquina (ransomware y eventos de seguridad). Dedup: no se crea alerta duplicada si ya existe una con los mismos campos clave en los últimos 10 minutos. Incluye `extensionDetectada` para identificar la extensión sospechosa que disparó la alerta |
| `cyberwatch_instancias/{machineId}/historial_navegacion/` | Subcollection de historial de navegación por máquina. Cada documento contiene: `url`, `titulo`, `fecha_visita`, `navegador` (chrome/edge/firefox), `perfil`, `sincronizado`. Sync cada 30 minutos, solo entradas nuevas |
| `config/ciberseguridad` | Configuración global: versión actual y URL de descarga para actualizaciones |

> **Nota:** El Dashboard lee alertas de todas las máquinas usando `CollectionGroup("alertas")`. Requiere un **single field index exemption** en Firestore para el campo `fechaHora` con scope "Collection group" (Descending).

> **Índice compuesto requerido:** La deduplicación de alertas usa `WhereEqualTo("nombreProceso") + WhereGreaterThanOrEqualTo("fechaHora")`, lo que requiere un **composite index** en la subcollección `alertas` (campos: `nombreProceso` ASC, `fechaHora` DESC). Firestore lo solicita automáticamente en el error log con un link para crearlo.

### Campos de `cyberwatch_instancias`

| Campo | Origen | Descripción |
|---|---|---|
| `id`, `hostname`, `version` | Service | Identificación |
| `ip_local`, `ultima_conexion` | Service | Conectividad |
| `lat`, `lon`, `ciudad`, `pais`, `isp`, `ultima_geolocalizacion` | Service | Geolocalización por IP |
| `lat_gps`, `lon_gps`, `precision_gps`, `ultima_ubicacion_gps` | UserAgent | GPS via Windows Location API |
| `comando`, `comando_estado`, `comando_resultado` | Dashboard → Service | Comandos remotos (`actualizar_agente`, etc.) |
| `comando_ua`, `comando_ua_estado` | Dashboard → UserAgent | Comandos al UserAgent (`captura`) |
| `ultima_captura_url`, `ultima_captura_ts`, `ultima_captura_motivo` | UserAgent | Última captura de pantalla (URL firmada en Storage) |
| `ultima_sync_historial` | UserAgent | Timestamp de la última sincronización de historial de navegación |

### Comandos remotos disponibles

| Comando | Destino | Acción |
|---|---|---|
| `actualizar_agente` | Service | Descarga el ZIP del release, extrae, reemplaza binarios, reinicia servicio y UserAgent. Post-reinicio: fuerza creación de tarea en Task Scheduler, ejecuta UserAgent, reporta log del batch en `comando_resultado` |
| `captura` | UserAgent | Toma screenshot, lo guarda en `%LOCALAPPDATA%\CyberWatch\capturas\`, lo sube a Firebase Storage y escribe la URL firmada en Firestore |

---

## Named Pipe

- **Nombre:** `CyberWatch_AgentPipe`
- **Servidor:** `AgentePipeServerService` (Service, Session 0, SYSTEM — acepta `AuthenticatedUsers`)
- **Cliente:** `PipClientService` (UserAgent)
- **Protocolo:** JSON `{"tipo":"amenaza","proceso":"<nombre>"}` — el Service notifica al UserAgent cuando detecta una amenaza

---

## Logs

| Archivo | Ubicación | Componente |
|---|---|---|
| `cyberwatch_service.log` | `C:\Program Files\CyberWatch\` | Service (requiere admin para escribir) |
| `cyberwatch_useragent.log` | `%LOCALAPPDATA%\CyberWatch\` | UserAgent |
| `cyberwatch_ua_crash.txt` | `%TEMP%\` | Crash del UserAgent antes de que inicie el host |
| `cw_update.log` | `C:\Windows\Temp\` (SYSTEM) | Script `.bat` de actualización remota (log detallado con exitcodes). También se reporta en `comando_resultado` de Firestore |

### Prefijos de log diagnóstico

| Prefijo | Componente | Información |
|---|---|---|
| `[Servicio]` | `ServicioCyberWatch` | Ciclo de detección: cantidad de eventos, procesos involucrados, amenazas detectadas con detalle (extensión, escrituras, renombrados) |
| `[Monitor]` | `MonitorActividadArchivos` | Eventos individuales de archivo (Created/Renamed), alertas de buffer overflow, extensiones sospechosas detectadas |
| `[RegistroInstancia]` | `RegistroInstanciaFirebaseService` | Datos enviados a Firestore: hostname, versión, IP, geo, BitLocker, Firewall, admins locales |
| `[Comando]` | `EjecutorTareasFirebaseService` | Recepción, ejecución y resultado de comandos remotos (`actualizar_agente`, etc.) con timestamps y tamaño de descarga |
| `[Historial]` | `HistorialNavegacionService` | Sincronización de historial de navegación: navegadores detectados, perfiles encontrados, entradas sincronizadas |

---

## Despliegue

Ver [docs/DEPLOY.md](docs/DEPLOY.md) para la guía completa.

### Resumen rápido

**Producción (GitHub Actions):**

1. Commitear y pushear cambios
2. Disparar el workflow manualmente desde GitHub Actions → Actions → `build-and-deploy` → `Run workflow` (ingresar versión, ej. `v3.9.0`)
3. El workflow: compila Service + UserAgent, embebe credenciales en `appsettings.json`, crea ZIP, sube a GitHub Releases, actualiza `config/ciberseguridad` en Firestore
4. En la máquina destino: el Service detecta la nueva versión via listener Firestore y ejecuta `actualizar_agente` automáticamente, o bien se envía el comando desde el Dashboard

**Primera instalación en una máquina:**

```powershell
# Como administrador
.\install.bat
```

---

## Machine ID

El ID de máquina se deriva del UUID de hardware via WMI (`Win32_ComputerSystemProduct`), lo que garantiza estabilidad entre reinstalaciones. Fallback: GUID guardado en `cyberwatch_machine_id.txt`.

---

## Pendientes / Estado actual

### Bugs resueltos (v4.3.0+)

- [x] **`cyberwatch_useragent.log` no se genera** — resuelto: log path movido a `%LOCALAPPDATA%\CyberWatch\`.
- [x] **`comando_ua` no desaparece de Firestore** — resuelto: credenciales embebidas como `CredentialJson` + `GetEffectiveCredentialPath()` escribe temp file.
- [x] **Captura falla con error GDI+** — resuelto: directorio de capturas movido de `C:\Program Files\CyberWatch\capturas\` a `%LOCALAPPDATA%\CyberWatch\capturas\` (el UserAgent corre como usuario normal sin permisos de escritura en Program Files).
- [x] **UserAgent no reinicia tras actualización remota** — resuelto: el Service ahora fuerza la creación de la tarea en Task Scheduler (`schtasks /Create /F`) y la ejecuta (`schtasks /Run`) en el bloque post-reinicio. Además, el log del batch script se reporta en `comando_resultado` de Firestore.
- [x] **FileSystemWatcher InternalBufferOverflow** — resuelto: buffer aumentado de 8KB (default) a 256KB (`InternalBufferSize = 262144`). Se agregó handler de `watcher.Error` para loguear overflows.
- [x] **Eventos acumulándose infinitamente en ConcurrentBag** — resuelto: nuevo método `TomarSnapshotYLimpiar()` que intercambia atómicamente el bag cada ciclo de detección, evitando acumulación sin límite.
- [x] **Race condition en machineId de FirebaseAlertService** — resuelto: el constructor leía `cyberwatch_machine_id.txt` antes de que `RegistroInstanciaFirebaseService` lo creara, resultando en `_machineId = ""` y alertas que nunca se enviaban. Fix: lectura lazy con `GetMachineId()` en el primer envío de alerta.
- [x] **Alertas sin detalle de extensión** — resuelto: nuevo campo `extensionDetectada` en `ReporteAmenaza`, `Alerta` y Firestore, que identifica qué extensión sospechosa disparó la alerta (ej. `.encrypted`, `.locked`).

### Refactoring pendiente

- [x] Dashboard migrado a React en `Front/` (Vite + TypeScript + Firestore)
- [ ] Ítem 7: ~~Dashboard monolítico~~ (opcional: retirar `CyberWatch.Dashboard` .NET si ya no se usa)
- [ ] Ítem 8: Consistencia en signed URLs de Firebase Storage
- [x] `MonitorActividadArchivos.Eventos` sin límite de tamaño — resuelto con `TomarSnapshotYLimpiar()`

### Problemas conocidos

- **Falsos positivos de procesos de Windows** — procesos del sistema como Windows Update generan actividad de archivos (ej. `store.db-journal` con 119+ eventos) que puede superar los umbrales de detección. El `RastreadorProcesos` actual usa `Process.Modules` que solo lista DLLs cargadas, no archivos escritos por un proceso, causando que todos los eventos reporten proceso como "Desconocido".
- **RastreadorProcesos limitado** — `Process.Modules` no puede rastrear qué proceso escribió un archivo específico. Para una correlación precisa se necesitaría ETW (Event Tracing for Windows) o un minifilter driver.
