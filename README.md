# CyberWatch

Servicio Windows de detección de ransomware con sincronización en tiempo real a Firebase Firestore.
Desarrollado en C# / .NET 8.0.

---

## Arquitectura

CyberWatch se compone de dos procesos que corren en paralelo:

| Componente | Sesión | Privilegio | Rol |
|---|---|---|---|
| **CyberWatch.Service** | Session 0 | SYSTEM | Detección de amenazas, respuesta activa (kill + cuarentena), sync Firestore, geolocalización IP, servidor Named Pipe |
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
| `cyberwatch_instancias/{machineId}` | Registro de cada máquina: hostname, IP, versión, geolocalización IP y GPS, última conexión, estado del servicio Windows vía `sc query`, comando remoto |
| `cyberwatch_instancias/{machineId}/alertas/` | Subcollection de alertas por máquina (ransomware y eventos de seguridad). Dedup: no se crea alerta duplicada si ya existe una con los mismos campos clave en los últimos 10 minutos. Incluye `extensionDetectada` para identificar la extensión sospechosa que disparó la alerta |
| `cyberwatch_instancias/{machineId}/logs_amenazas/` | **Auditoría de detecciones ransomware:** un documento por cada ciclo en que se detecta amenaza (append-only). Incluye repeticiones aunque no se cree nueva entrada en `alertas` por dedup; campos `alertaFirestoreCreada`, `eventosArchivoEnCiclo`, `resumen`, cuarentena, etc. El dashboard consulta con `CollectionGroup("logs_amenazas")` |
| `cyberwatch_instancias/{machineId}/historial_navegacion/` | Subcollection de historial de navegación por máquina. Cada documento contiene: `url`, `titulo`, `fecha_visita`, `navegador` (chrome/edge/firefox), `perfil`, `sincronizado`. Sync cada 30 minutos, solo entradas nuevas |
| `config/ciberseguridad` | Configuración global: versión actual y URL de descarga para actualizaciones |
| `cyberwatch_logs/` | **Logs centralizados:** Service, UserAgent (vía `FirestoreLoggerProvider`) y MiniAgente escriben aquí; el dashboard (`Front`, ruta `/logs`) muestra la tabla en tiempo real con `onSnapshot` |

> **Nota:** El Dashboard lee alertas de todas las máquinas usando `CollectionGroup("alertas")`. Requiere un **single field index exemption** en Firestore para el campo `fechaHora` con scope "Collection group" (Descending).

> **Índice `logs_amenazas`:** Ejecutá `firebase deploy --only firestore:indexes` (collection group `logs_amenazas` con `fechaHora` DESC y `__name__` DESC). Sin esto, la página **Alertas** (consulta global) falla; a veces el SDK muestra `permission-denied` en lugar de “falta índice”. Las reglas deben permitir lectura: `firebase deploy --only firestore:rules`.

> **Índice compuesto requerido:** La deduplicación de alertas usa `WhereEqualTo("nombreProceso") + WhereGreaterThanOrEqualTo("fechaHora")`, lo que requiere un **composite index** en la subcollección `alertas` (campos: `nombreProceso` ASC, `fechaHora` DESC). Firestore lo solicita automáticamente en el error log con un link para crearlo.

### Campos de `cyberwatch_instancias`

| Campo | Origen | Descripción |
|---|---|---|
| `id`, `hostname`, `version` | Service | Identificación |
| `ip_local`, `ultima_conexion` | Service | Conectividad |
| `servicio_sc_estado`, `servicio_sc_detalle`, `servicio_sc_salida`, `servicio_sc_consultado` | Service | Resultado de `sc query` sobre `App:ServiceName` (estado Windows del servicio para el dashboard) |
| `lat`, `lon`, `ciudad`, `pais`, `isp`, `ultima_geolocalizacion` | Service | Geolocalización por IP |
| `lat_gps`, `lon_gps`, `precision_gps`, `ultima_ubicacion_gps` | UserAgent | GPS via Windows Location API |
| `comando`, `comando_estado`, `comando_resultado` | Dashboard → Service | Comandos remotos (`actualizar_agente`, etc.) |
| `comando_ua`, `comando_ua_estado` | Dashboard → UserAgent | Comandos al UserAgent (`captura`, `historial_completo`) |
| `ultima_captura_url`, `ultima_captura_ts`, `ultima_captura_motivo` | UserAgent | Última captura de pantalla (URL firmada en Storage) |
| `ultima_sync_historial` | UserAgent | Timestamp de la última sincronización de historial de navegación |
| `ultima_historial_completo_url`, `ultima_historial_completo_ts` | UserAgent | URL firmada del último JSON de historial completo exportado |
| `ultima_historial_completo_error` | UserAgent | Si la última exportación de historial completo falló, mensaje de error (p. ej. "Storage no configurado", "Sin entradas", o excepción de subida) |

### Respuesta activa: kill + cuarentena

Cuando se detecta una amenaza, el Service ejecuta la siguiente cadena de respuesta:

1. **Resolver ruta del ejecutable** — obtiene `MainModule.FileName` del proceso mientras está vivo
2. **Alerta local** — log en `cyberwatch_service.log`
3. **Matar proceso** — `Process.Kill()` sobre todas las instancias del proceso (`LiquidarProcesos`)
4. **Cuarentena** — mueve el ejecutable a `C:\ProgramData\CyberWatch\Cuarentena\` con nombre `{timestamp}_{nombre}.quarantine` (`ServicioCuarentena`)
5. **Alerta Firestore** — envía alerta incluyendo resultado de cuarentena (`rutaEjecutableOriginal`, `rutaCuarentena`, `cuarentenaExitosa`, `cuarentenaError`)
6. **Notificar UserAgent** — via Named Pipe para captura de pantalla

**Configuración de cuarentena** (en `appsettings.json`, sección `Umbrales`):
- `CarpetaCuarentena`: ruta de la carpeta de cuarentena (default: `C:\ProgramData\CyberWatch\Cuarentena`)
- `DirectoriosProtegidos`: lista de directorios cuyos ejecutables NO se mueven a cuarentena (default: `C:\Windows`, `C:\Program Files\CyberWatch`)

**Protecciones:** no se mueven archivos de directorios protegidos (Windows, CyberWatch propio). Si el archivo está bloqueado tras el kill, se reintenta a los 500ms. Si la ruta no se pudo resolver (proceso ya terminó), la cuarentena se omite pero la alerta se envía igual con el error.

### Detección de ransomware: umbrales y exclusiones

La detección se basa en umbrales (cantidad de escrituras/renombrados en una ventana de tiempo y extensiones sospechosas). Para evitar falsos positivos con procesos legítimos (indexador de Windows, OneDrive, servicios del sistema, etc.), el Service usa una **lista de procesos excluidos** configurable.

- **Configuración:** en `appsettings.json` del Service, sección `Umbrales` → `ProcesosExcluidos`: lista de nombres de proceso (con o sin `.exe`, comparación sin distinguir mayúsculas). Si un proceso está en esa lista, no se genera alerta aunque supere los umbrales.
- **Valores por defecto:** incluyen `SearchIndexer`, `SearchProtocolHost`, `svchost`, `OneDrive`, `GoogleDriveFS`, `MsMpEng`, `RuntimeBroker`, etc. Se pueden añadir más en la config según necesidad.

### Comandos remotos disponibles

| Comando | Destino | Acción |
|---|---|---|
| `actualizar_agente` | Service | Descarga el ZIP del release, extrae, lanza batch de actualización (`Process.Start` directo, sin Task Scheduler), reinicia servicio y UserAgent. Post-reinicio: fuerza creación de tarea en Task Scheduler, ejecuta UserAgent, reporta log del batch en `comando_resultado` |
| `reiniciar_servicio` | Service | Lanza batch que hace `net stop` + `net start` del servicio. Útil para aplicar cambios de configuración sin actualizar binarios |
| `captura` | UserAgent | Toma screenshot, lo guarda en `%LOCALAPPDATA%\CyberWatch\capturas\`, lo sube a Firebase Storage y escribe la URL firmada en Firestore |
| `historial_completo` | UserAgent | Lee todo el historial de Chrome/Edge/Firefox, lo exporta como JSON a `%LOCALAPPDATA%\CyberWatch\historial\`, lo sube a Firebase Storage y escribe la URL firmada en Firestore. Si falla (sin entradas, Storage no configurado, error de subida), escribe el mensaje en `ultima_historial_completo_error`. Ver [docs/NAVEGACION.md](docs/NAVEGACION.md) |

---

## Named Pipe

- **Nombre:** `CyberWatch_AgentPipe`
- **Servidor:** `AgentePipeServerService` (Service, Session 0, SYSTEM — acepta `AuthenticatedUsers`)
- **Cliente:** `PipClientService` (UserAgent)
- **Protocolo:** JSON `{"tipo":"amenaza","proceso":"<nombre>"}` — el Service notifica al UserAgent cuando detecta una amenaza
- **Keepalive:** el servidor envía `{"tipo":"ping"}` cada 20 segundos para mantener la conexión activa y detectar desconexiones

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
| `[Cuarentena]` | `ServicioCuarentena` | Resultado de mover ejecutable malicioso a cuarentena: ruta origen, ruta destino, errores (directorio protegido, acceso denegado, archivo no encontrado) |
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

**Primera instalación (o reinstalación manual) en una máquina:**

```powershell
# Como administrador — desde la carpeta con los binarios
.\install.bat
```

El `install.bat` ejecuta estos pasos automáticamente:
1. Detiene el Service (`net stop`) y UserAgent (`taskkill`)
2. Copia todos los archivos a `C:\Program Files\CyberWatch\`
3. Registra/actualiza el Windows Service (`sc create`/`sc config` + `net start`)
4. Espera 5 segundos para inicialización
5. Lanza el UserAgent directamente (`start`)

**Reglas de Firestore (firebase.json):** Para poder ejecutar `firebase deploy --only firestore:rules` desde la raíz del repo, `firebase.json` debe incluir la sección `firestore` con la ruta a las reglas, por ejemplo: `"firestore": { "rules": "firestore.rules" }`. Sin esa sección, el CLI no encuentra el target y falla.

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
- [x] **Race condition en `RastreadorProcesos.LimpiarCache()`** — resuelto: `Dictionary` reemplazado por `ConcurrentDictionary`. El crash (`Collection was modified`) mataba el servicio por `BackgroundServiceExceptionBehavior: StopHost`.
- [x] **`InternalBufferOverflow` en `C:\`** — resuelto: el monitor de archivos ahora monitorea solo `C:\Users\` en el drive del sistema (en vez de la raíz completa), eliminando el ruido de `Windows\`, `ProgramData\`, etc. Drives de red removidos del monitoreo.
- [x] **`EventLog access is not supported` al correr con `dotnet run`** — resuelto: `logging.ClearProviders()` en `Program.cs` del Service para evitar el `EventLogLoggerProvider` que `CreateDefaultBuilder` agrega automáticamente.
- [x] **Doble ejecución de `actualizar_agente` (v4.7.0)** — el listener de Firestore disparaba el comando dos veces: la segunda ejecución arrancaba después de que la primera liberaba el lock (`_ejecutando = 0`), lanzando un segundo bat que hacía `net stop` al service recién reiniciado. Fix: `await Task.Delay(Timeout.Infinite, ct)` tras lanzar el script, manteniendo el lock hasta que el CancellationToken cancele al apagarse el service.
- [x] **Batch de actualización nunca se ejecutaba (v5.6.0)** — `schtasks /Create` + `schtasks /Run` reportaban éxito pero el batch no corría realmente (problema de formato de fecha/hora y comportamiento inconsistente de Task Scheduler). Fix: eliminado Task Scheduler completamente, ahora se usa `Process.Start` directo del `.bat`. Los procesos hijos de un Windows Service sobreviven al `net stop` (SCM solo detiene el servicio, no mata el árbol de procesos).
- [x] **`FirebaseAlertService` fallaba en producción (v5.6.0)** — usaba `GoogleCredential.FromFile(_settings.CredentialPath)` pero en deploy `CredentialPath` es `""` (las credenciales se embeben como `CredentialJson`). Fix: usa `GetEffectiveCredentialPath()` que escribe un archivo temporal desde `CredentialJson`.
- [x] **Storage (capturas/historial) no funcionaba en producción (v5.6.0)** — `GoogleCredential.FromFile(credPath)` causaba file lock cuando múltiples servicios accedían al mismo archivo temporal de credenciales. Fix: `FromStream` con `using` para liberar el handle inmediatamente.
- [x] **UserAgent crasheaba por `COMException` en GPS (v5.6.0)** — `Geolocator.RequestAccessAsync()` lanzaba `COMException (0x80070006)` (handle inválido) intermitentemente, y como no estaba en try/catch, mataba todo el host. Fix: try/catch alrededor de la inicialización del Geolocator, degrada gracefully (GPS desactivado para esa sesión).
- [x] **`install.bat` no iniciaba UserAgent (v5.6.0)** — el instalador solo registraba el Windows Service y dependía de que el Service registrara la tarea de Task Scheduler. Fix: `install.bat` ahora lanza el UserAgent directamente con `start` además de registrar el Service.

### Refactoring pendiente

- [x] Dashboard migrado a React en `Front/` (Vite + TypeScript + Firestore)
- [ ] Ítem 7: ~~Dashboard monolítico~~ (opcional: retirar `CyberWatch.Dashboard` .NET si ya no se usa)
- [ ] Ítem 8: Consistencia en signed URLs de Firebase Storage
- [x] `MonitorActividadArchivos.Eventos` sin límite de tamaño — resuelto con `TomarSnapshotYLimpiar()`

### Problemas conocidos

- **Falsos positivos de procesos de Windows** — procesos del sistema como Windows Update generan actividad de archivos (ej. `store.db-journal` con 119+ eventos) que puede superar los umbrales de detección. **Mitigación:** lista de **procesos excluidos** (`Umbrales:ProcesosExcluidos` en appsettings del Service): no se generan alertas para esos nombres (p. ej. SearchIndexer, OneDrive, svchost). Se pueden añadir más según necesidad. El `RastreadorProcesos` actual usa `Process.Modules` que solo lista DLLs cargadas, no archivos escritos por un proceso, causando que algunos eventos reporten proceso como "Desconocido". (Mitigado parcialmente: el monitor ahora solo observa `C:\Users\` en vez de `C:\` completo).
- **RastreadorProcesos limitado** — `Process.Modules` no puede rastrear qué proceso escribió un archivo específico. Para una correlación precisa se necesitaría ETW (Event Tracing for Windows) o un minifilter driver.
