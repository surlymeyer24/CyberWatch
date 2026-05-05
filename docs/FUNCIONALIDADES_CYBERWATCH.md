# CyberWatch — Catálogo detallado de funcionalidades

Este documento describe las capacidades del sistema **CyberWatch** tal como están implementadas en el repositorio (servicio Windows, agente de usuario, integración Firebase y herramientas auxiliares). Sirve como referencia funcional para operadores, desarrolladores y auditoría.

---

## 1. Visión general

**CyberWatch** es una solución de **ciberseguridad orientada a endpoints Windows** que combina:

1. **Detección heurística de comportamiento tipo ransomware** (actividad masiva de archivos + extensiones típicas de cifrado).
2. **Respuesta activa** (terminación de proceso y cuarentena del ejecutable bajo reglas estrictas).
3. **Telemetría y comando remoto** vía **Google Firebase** (Firestore + Storage).
4. **Monitoreo complementario del Event Log de Windows** (Defender, privilegios, intentos de acceso).
5. **Agente en sesión de usuario** para GPS, capturas de pantalla, historial de navegación y recepción de órdenes desde el panel.

Arquitectura resumida: **CyberWatch.Service** corre en **Session 0** como **SYSTEM**; **CyberWatch.UserAgent** corre en la **sesión del usuario** sin ventana visible y se registra para ejecutarse al inicio de sesión.

---

## 2. CyberWatch.Service (servicio Windows)

### 2.1 Monitor de actividad de archivos (ETW)

- **Tecnología:** sesión **ETW** (Event Tracing for Windows) con proveedor de kernel **FileIO** / **FileIOInit** (no FileSystemWatcher sobre carpetas completas).
- **Nombre de sesión:** `CyberWatchETW`. Al iniciar, si existe una sesión previa con el mismo nombre, se cierra para evitar conflictos.
- **Ámbito geográfico de paths:**
  - En el **disco del sistema**: solo se consideran eventos bajo **`X:\Users\`** (reduce ruido de `Windows\`, `ProgramData\`, etc.).
  - En **otros discos fijos**: se monitorea la **raíz del volumen** completa.
- **Eventos capturados:**
  - **Creación de archivo** (`FileIOCreate`): se registra; si la extensión coincide con la lista de extensiones sospechosas, el evento se marca como **extensión sospechosa**.
  - **Escritura** (`FileIOWrite`): un **máximo de un evento por par (proceso, archivo)** por ciclo de evaluación (deduplicación para no inflar conteos).
  - **Renombrado** (`FileIORename`): siempre cuenta; también puede marcarse como extensión sospechosa si el nombre final tiene extensión típica de ransomware.
- **Metadatos por evento:** nombre de proceso, PID resuelto a **ruta del ejecutable** (`MainModule.FileName` cuando es posible), ruta del archivo, tipo de evento, marca temporal.
- **Exclusión en el monitor:** procesos cuya coincidencia de nombre está en `**Umbrales:ProcesosExcluidos`** no generan eventos en el bag (no entran al ciclo de detección desde el monitor).

### 2.2 Motor de detección (`EvaluadorAmenazas`)

- **Entrada:** lista de `EventoArchivo` del ciclo actual (snapshot tomado y el acumulador se limpia en cada iteración del servicio principal).
- **Exclusión lógica:** si el nombre de proceso (con o sin `.exe`) está en `**ProcesosExcluidos`**, no se evalúa como amenaza.
- **Umbrales configurables** (`Umbrales` en `appsettings.json`):
  - `**MaxEscrituraPermitida`** (por defecto 500): cantidad mínima de eventos de escritura (tras deduplicación por archivo) del mismo proceso en el ciclo para considerar “escrituras sospechosas”.
  - `**MaxRenombradosPermitidos`** (por defecto 100): renombrados del mismo proceso en el ciclo.
  - `**UmbralPuntuacionAmenaza**` (por defecto 3): puntuación mínima para declarar amenaza.
- **Sistema de puntuación:**
  - Extensión sospechosa en cualquier evento del proceso: **+3** (indicador fuerte).
  - Escrituras sospechosas **y** renombrados sospechosos a la vez: **+3** (patrón clásico ransomware).
  - Solo escrituras altas o solo renombrados altos: **+1** cada uno (más débil, puede ser aplicación legítima).
- **Refuerzo opcional por entropía** (`CalculadorEntropia`, **desactivado por defecto** con `Umbrales:EntropiaHabilitada=false`): entropía de Shannon (bits por byte, 0–8) sobre una **muestra** del primer archivo de escritura/creación representativo del proceso en el ciclo (se evitan como preferencia las extensiones en `ExtensionesEntropiaAltaEsperada`). Solo puede sumar `EntropiaBonusPuntos` si la puntuación base ya es ≥ `EntropiaMinimoPuntuacionBase`, sigue por debajo de `UmbralPuntuacionAmenaza`, y —si `EntropiaRequierePatronRansomware` es true— ya hay patrón (escrituras/renombrados por umbral o extensión sospechosa). **No** declara amenaza solo por entropía sin señal de comportamiento previa.
- **Extensiones sospechosas:** lista configurable (`.encrypted`, `.locked`, `.wncry`, etc.) en `Umbrales:ExtensionesSospechosas`.
- **Salida:** `ReporteAmenaza` con banderas de escrituras/renombrados/extensión, **extensión detectada** concreta, puntuación, y opcionalmente `EntropiaMuestra` / `EntropiaAplicadaComoBonus` / ruta de muestra para auditoría.

### 2.3 Bucle principal (`ServicioCyberWatch`)

- **Intervalo:** `Task.Delay` según `**Umbrales:IntervaloTiempoSeg**` (por defecto 10 s).
- **Fases:**
  1. `TomarSnapshotYLimpiar()` del monitor (evita crecimiento ilimitado de memoria).
  2. Evaluación por cada proceso distinto en el snapshot.
  3. **Protección “falso positivo masivo”:** si el número de amenazas distintas en un ciclo supera `**MaxAmenazasPorCiclo**` (por defecto 3), **no hay respuesta activa** (kill/cuarentena); solo se persiste telemetría en Firestore con el motivo en el resultado de cuarentena.
  4. Por cada amenaza:
    - **Directorios protegidos:** si la ruta del ejecutable comienza por alguna entrada de `**DirectoriosProtegidos**` (`C:\Windows`, `Program Files`, etc.), **no** se liquida ni cuarentena; se registra alerta con error explicativo.
    - **Cooldown:** si el mismo nombre de proceso fue liquidado hace menos de `**CooldownLiquidacionMinutos**`, se omite la respuesta activa (evita bucles kill → respawn).
    - **Respuesta activa** si aplica: alerta local (`GestorAlertas`), liquidación de proceso, registro de cooldown.
    - **Cuarentena graduada:** la **primera** detección de un proceso en ventana de 5 minutos: solo **kill**, sin mover a cuarentena (mensaje explícito en resultado). Si **reincide** dentro de 5 minutos desde la primera detección, se ejecuta `**ServicioCuarentena**` y se limpia el seguimiento de “primera detección”.
  5. Envío a Firestore (`FirebaseAlertService`) y, si hubo respuesta activa, notificación al UserAgent por **Named Pipe**.

### 2.4 Respuesta: liquidación y cuarentena

- `**LiquidarProcesos`:** termina **todas** las instancias del proceso cuyo nombre coincide con la amenaza (`Process.Kill()`).
- `**ServicioCuarentena`:** mueve el ejecutable a `**Umbrales:CarpetaCuarentena**` (por defecto `C:\ProgramData\CyberWatch\Cuarentena`) con nombre `{timestamp}_{nombre}.quarantine`. Respeta directorios protegidos; puede reintentar si el archivo estaba bloqueado (comportamiento documentado en README).
- `**GestorAlertas`:** escribe una línea de texto en `**cyberwatch.log**` en el directorio de trabajo del proceso (alerta local además de Firestore).

### 2.5 Integración Firebase — alertas ransomware

- `**FirebaseAlertService`:** inicializa **Firebase Admin** con credencial de servicio (archivo o JSON embebido vía `GetEffectiveCredentialPath()`).
- **Machine ID:** lectura **lazy** desde `MachineIdHelper` / archivo generado por el registro de instancia (evita alertas perdidas por orden de arranque).
- **Subcolección `alertas`:** documento por alerta con deduplicación: **no** se crea otra alerta si ya existe una con el **mismo `nombreProceso**` y `**fechaHora` ≥ ahora − 10 minutos**.
- **Subcolección `logs_amenazas`:** **siempre** se agrega un documento por ciclo en que se detecta amenaza (auditoría **append-only**), indicando si la alerta en `alertas` fue creada o omitida por dedup, conteo de eventos en ciclo, resumen textual, datos de cuarentena, etc.
- Campos típicos en alerta: proceso, fecha, escrituras/renombrados, extensión sospechosa, **extensión detectada**, rutas, resultado de cuarentena, origen `CyberWatch.Service`, hostname, machine id; si aplicó bonus de entropía: `entropiaMuestra`, `entropiaAplicadaComoBonus` (también en `logs_amenazas`).

### 2.6 Registro periódico de la instancia (`RegistroInstanciaFirebaseService`)

- **Frecuencia:** cada `**Firebase:IntervaloRegistroInstanciaMinutos**` (por defecto 5). Si el intervalo es ≤ 0, el servicio no registra.
- **Identificación de máquina:** prioridad al **UUID de hardware** WMI (`Win32_ComputerSystemProduct`); si no es válido, fallback a `**cyberwatch_machine_id.txt**` con GUID persistente.
- **Datos enviados a `cyberwatch_instancias/{machineId}**` (merge de campos):
  - Hostname, versión de aplicación (`CyberWatch:Version`), nombre del servicio Windows, **última conexión**.
  - **IP local**, **IP pública** (cuando se obtiene).
  - **Geolocalización por IP** (lat, lon, ciudad, país, ISP, timestamp de última geo) — recalculada como máximo cada **30 minutos** si ya había cache.
  - **BitLocker activo**, **Firewall de Windows activo**, **lista de administradores locales**.
  - Estado del servicio vía `**sc query**`: `servicio_sc_estado`, `servicio_sc_detalle`, `servicio_sc_salida`, `servicio_sc_consultado`.

### 2.7 Ejecutor de comandos remotos (`EjecutorTareasFirebaseService`)

- **Mecanismo:** **listener en tiempo real** sobre el documento de la instancia en Firestore.
- **Campos:** al recibir un comando no vacío en `**comando**`, se limpia el campo, se pone `**comando_estado**` en `ejecutando` y luego `completado` / `error` / `reiniciando` según el caso; `**comando_resultado**` describe el resultado.
- **Comandos implementados:**
  - `**actualizar_agente`:** lee `**config/ciberseguridad**` en Firestore (`url_descarga`, opcionalmente `version`), descarga ZIP, extrae, genera y ejecuta un **batch** de actualización (detiene servicio, copia archivos, reinicia), deja estado `reiniciando`; tras el reinicio el servicio reconcilia estado, lee `cw_update.log`, **recrea/ejecuta** el UserAgent si el exe existe y completa el resultado en Firestore. Incluye mitigación contra **doble ejecución** del listener (lock hasta apagado del host tras lanzar actualización).
  - `**reiniciar_servicio`:** script que hace **net stop / net start** del servicio configurado.
  - `**sc_query`:** ejecuta consulta inmediata al servicio Windows y actualiza los campos `**servicio_sc_***` en el documento de instancia.
- **Post-actualización:** asegura tarea de usuario y ejecución del UserAgent cuando corresponde (coordinado con `RegistradorTareaUsuarioService` / rutas de instalación).

### 2.8 Registro de tarea para el UserAgent (`RegistradorTareaUsuarioService`)

- Al arrancar el Service, si existe `**CyberWatch.UserAgent.exe**` junto al servicio y no existe ya la tarea `**CyberWatch\UserAgent**`, registra en **Task Scheduler** una tarea XML para que el UserAgent arranque **al inicio de sesión** de cualquier usuario.

### 2.9 Monitor de eventos de seguridad (`SecurityEventMonitorService`)

- **Periodicidad:** cada **2 minutos** (si Firebase está configurado y hay `machineId`).
- **Fuentes y Event IDs:**
  - **1116** — `Microsoft-Windows-Windows Defender/Operational`: malware detectado por Defender → alerta tipo `malware_detectado`.
  - **7036** — `System`: servicio detenido, filtrado por mensajes que indiquen **Windows Defender / WinDefend** detenido → `defender_detenido`.
  - **4732** — `Security`: usuario agregado al grupo **Administradores** → `admin_agregado`.
  - **4625** — `Security`: inicio de sesión fallido; si hay **≥ 5** intentos en ventana de **5 minutos** (contador en memoria), se envía **una** alerta tipo `brute_force` por ráfaga.
- **Persistencia:** mismas subcolección `**alertas**` con `**Origen` = `SecurityEventMonitor**`, deduplicación por `**tipo` + `eventoId**` en los últimos 10 minutos.
- **Resumen en instancia:** actualiza `**alertas_sistema**` en el documento de la máquina (últimas 10 entradas resumidas).

### 2.10 Monitor de servicios no-base (`ServiciosDesconocidosService` / `AnalizadorServicios`)

- **Objetivo:** detectar **servicios Windows SCM** instalados que **no** forman parte de una **whitelist base** embebida (`Data/servicios_base_windows.json`, ≥150 nombres, recurso embebido en el ensamblado del Service) y que **no** estén en exclusiones operativas.
- **Enumeración:** `ServiceController.GetServices()`; **ImagePath** desde registro `HKLM\SYSTEM\CurrentControlSet\Services\{nombre}` → normalización con `Environment.ExpandEnvironmentVariables`, comillas y fragmentos habituales antes de persistir.
- **Periodicidad:** cada `**Umbrales:IntervaloServiciosMinutos**` (por defecto **10** minutos).
- **Exclusiones configurables:** lista `**Umbrales:ServiciosExcluidos**` (nombres cortos SCM, comparación sin distinguir mayúsculas). Sirve para software interno no cubierto por la whitelist estática.
- **Estado entre ciclos:** en memoria se guarda el conjunto de nombres **no-base** vistos en el **ciclo anterior**. Un servicio es **nuevo entre ciclos** (`EsNuevo`) si es no-base en este ciclo y **no** estaba en ese conjunto en el ciclo previo (no se compara contra “todos los servicios del SO”).
- **Mitigación de ráfaga en el primer arranque:** si `**Umbrales:SuprimirAlertasPrimerCicloServicios**` es **true** (por defecto), el **primer ciclo** tras el arranque **persiste** documentos en Firestore pero **no crea alertas** en `alertas`, para evitar decenas de alertas cuando `_desconocidosCicloAnterior` está vacío. Los ciclos siguientes sí pueden alertar cuando un no-base es “nuevo” respecto al ciclo anterior.
- **Firestore — subcolección `servicios_desconocidos`:** ruta `**cyberwatch_instancias/{machineId}/servicios_desconocidos/{nombreServicio}**` (ID ≈ nombre SCM); **merge** en cada ciclo con campos como nombre, display, estado, tipo de inicio, ruta ejecutable, `machineId`, `hostname`, `fechaDeteccion`, `esNuevo`.
- **Alertas:** solo cuando `EsNuevo` y no está suprimido el ciclo; tipo `**servicio_desconocido_nuevo**` (`ServicioDesconocidoTipoAlerta.NuevoEntreCiclos`), campo `**nombreServicio**`, deduplicación ligera por tipo + nombre en ventana reciente.
- **Sin Firebase o sin `machineId`:** el hosted service no escribe en Firestore; queda en log local.
- **Limitaciones:** la whitelist **no cubre todas las SKU/versiones** de Windows; nombres pueden variar entre builds; **falsos positivos** en entornos corporativos se mitigan con `ServiciosExcluidos`; **ImagePath** puede incluir argumentos de línea de comandos — lo almacenado es la ruta normalizada según la lógica actual del servicio.

### 2.10.1 Monitor de firma Authenticode en servicios (`MonitorServiciosFirmaDigitalService`)

- **Objetivo:** comprobar **autenticidad/integridad** del binario del servicio (PE con firma) frente a mero nombre SCM; reduce falsos negativos por malware renombrado (`svchost.exe` falso, etc.). Complementa `ServiciosDesconocidosService` (lista estática de nombres), no la reemplaza.
- **Activo solo si** `**Umbrales:FirmaServiciosHabilitado**` es **true** (por defecto **false**). **Intervalo** típico **1 h** (`**IntervaloFirmaServiciosHoras**`) para no recorrer todos los `ServiceController` con frecuencia.
- **Alcance por ciclo:** servicios con estado **Running**; **ImagePath** desde el mismo registro que el monitor de no-base (`**ServiciosDesconocidosService.LeerImagePathRegistro**`); normalización y extracción del PE con `**ServicioWindowsPaths**` (comillas, variables, argumentos, extensiones `.exe`/`.dll`/`.sys`, prefijo `\??\`).
- **Validación:** `**IValidadorFirmaEjecutable**` / `**ValidadorFirmaEjecutableNet**` — `X509Certificate.CreateFromSignedFile` + `X509Chain.Build` (revocación en línea, timeout). No es paridad con **WinVerifyTrust**; evolución documentada en [iteracion-6-pasos.md](iteracion-6-pasos.md).
- **Filtros:** `**FirmaServiciosSoloNoBase**` (solo nombres SCM no incluidos en la whitelist embebida); `**ServiciosFirmaExcluidos**` (exclusiones ad hoc).
- **Alertas Firestore:** `**tipo**` = `**servicio_sin_firma_valida**` (`**ServicioFirmaDigitalTipoAlerta**`); `**razonFirma**` `sin_firma` o `cadena_invalida`; `**subjectFirma**` si se leyó certificado; `**rutaEjecutableOriginal**` = binario verificado; dedup por `tipo` + `nombreServicio` + ventana `**DedupFirmaServiciosHoras**`.
- **Sin Firebase:** solo log de advertencia cuando correspondería alertar.

### 2.10.2 Monitor de servicios anómalos / política remota (`MonitorServiciosAnomalos`, iteración 7)

- **Objetivo:** después de la firma local (iteración 6), aplicar **política centralizada** en Firestore: exclusiones por nombre SCM y lista blanca por **hash SHA-256** del PE, sin redesplegar agentes.
- **Activo solo si** `**Umbrales:MonitorServiciosAnomalosHabilitado**` es **true** (por defecto **false**). **Intervalo** por defecto **60 min** (`**IntervaloServiciosAnomalosMinutos**`).
- **Config remota:** documento **`config/servicios`** (`**ConfigServiciosFirestoreService**`, mismo patrón Listen que `config/red`): `nombres_excluidos`, `hashes_permitidos`, `ultima_modificacion`. Modelo [ConfigServiciosDocument](../CyberWatch.Shared/Models/ConfigServiciosDocument.cs); ejemplo [config-servicios.example.json](config-servicios.example.json). IDs: `Firebase:FirestoreConfigRedCollection` + `Firebase:FirestoreConfigServiciosDocumentId` (default `servicios`).
- **Enumeración:** WMI `**Win32_Service**` con `State='Running'`; **PathName** → `**ServicioWindowsPaths**` (igual filosofía que otros monitores de servicio).
- **Firma:** `**X509Certificate.CreateFromSignedFile**` → `**X509Certificate2**` → `**Verify()`** (sin cadena completa CRL como en iteración 6); si **Verify** es **true**, el servicio se considera OK en esta pasada.
- **Si la firma no verifica:** SHA-256 del archivo (streaming); si el hash está en la caché remota **o** el nombre SCM está en `nombres_excluidos`, **no** se alerta; si no, alerta `**servicio_no_firmado**` (`**ServicioAnomaloTipoAlerta.NoFirmado**`), campo `**hashEjecutableSha256**`, `**rutaEjecutableOriginal**`, dedup `**DedupServiciosAnomalosHoras**` con `tipo` + `nombreServicio` + `fechaHora`.
- **Coexistencia con iteración 6:** pueden solaparse alertas para el mismo servicio; en producción conviene habilitar solo uno u operar con umbrales/dashboard distintos.
- **Persistencia:** `**IFirebaseAlertService.AgregarAlertaInstanciaAsync**` tras dedup en el monitor (misma subcolección `alertas`).

### 2.10.3 Monitor de puertos TCP IPv4 (`PuertosAbiertosMonitorService`)

- **Objetivo:** visibilidad de sockets **TCP IPv4** que Windows tiene registrados (equivalente operativo a **netstat** / tabla extendida), **sin** escaneo activo de red.
- **API:** `iphlpapi.dll` → `GetExtendedTcpTable` con clase `TCP_TABLE_OWNER_PID_ALL`; conversión de puertos desde orden de red y dirección IPv4 textual.
- **Filtrado:** si `**Umbrales:MonitorearSoloListen**` es **true** (por defecto), solo filas en estado **Listen**; si es **false**, también **Established** (más ruido).
- **Listas embebidas:** `Data/puertos_base_windows.json` (puertos habituales del SO/red que no deben solos disparar alerta “nuevo”) y `Data/puertos_sospechosos.json` (puertos frecuentemente asociados a herramientas de admin/C2 en políticas restrictivas).
- **Config remota (`config/red`):** `ConfigRedFirestoreService` obtiene un snapshot inicial y escucha cambios con el mismo patrón **Listen** que los comandos remotos. En cada ciclo del monitor, la whitelist efectiva para “puerto permitido” es la **unión** de la lista embebida y `puertos_globales_permitidos` del documento. Los procesos listados en `procesos_red_excluidos` **no** generan entradas en `alertas` (sí se sigue persistiendo el socket en `puertos_abiertos`). `bloqueo_estricto_activo` se registra en log si está en true; **no** se terminan procesos en esta versión. Rutas configurables: `Firebase:FirestoreConfigRedCollection`, `Firebase:FirestoreConfigRedDocumentId`. Sin credencial Firebase o sin documento: solo listas embebidas.
- **Periodicidad:** `**Umbrales:IntervaloPuertosMinutos**` (por defecto **5** minutos).
- **Exclusiones:** `**Umbrales:PuertosExcluidos**` — no generan alerta por “nuevo entre ciclos” (software interno).
- **Estado entre ciclos:** conjunto de claves `estado|IP local|puerto|PID`; **es nuevo** si la clave no estaba en el ciclo anterior **y** el puerto no está en la lista base **ni** en los puertos remotos permitidos **y** no está en exclusiones locales.
- **Mitigación primer arranque:** `**SuprimirAlertasPrimerCicloPuertos**` (default **true**) — persiste documentos en Firestore pero no crea alertas en `alertas` en el primer ciclo.
- **Firestore:** subcolección `**puertos_abiertos**` (`**FirestoreCollectionPuertosAbiertos**`) con merge por documento (ID derivado de estado, IP, puerto, PID).
- **Alertas:** tipo `**puerto_sospechoso**` si el puerto está en la lista negra embebida; tipo `**puerto_nuevo_entre_ciclos**` si aplica la lógica de novedad (prioridad a sospechoso si ambos). Campos `**puertoLocal**`, `**pidProceso**` en `Alerta`; deduplicación por `tipo` + puerto + PID + ventana **10 minutos**.
- **Sin Firebase o sin `machineId`:** no escribe en Firestore; solo log local.

### 2.11 Servidor Named Pipe (`AgentePipeServerService`)

- **Nombre del pipe:** `CyberWatch_AgentPipe`.
- **Audiencia:** acepta clientes autenticados como usuarios del equipo (`AuthenticatedUsers` en implementación típica).
- **Protocolo:** líneas JSON; tipos relevantes:
  - `**{"tipo":"amenaza","proceso":"..."}**` — notificación al UserAgent para disparar captura contextual.
  - `**{"tipo":"ping"}**` — keepalive periódico (aprox. cada 20 s) para mantener la conexión viva.

#### 2.11.1 Auto-Protección / Anti-Tampering (iteración 8)

- **Recuperación SCM:** `**install.bat**` ejecuta `**sc failure CyberWatch reset= 0 actions= restart/5000/restart/5000/restart/5000**` tras crear o reconfigurar el servicio (antes de `net start`).
- **Lock compartido:** `**C:\ProgramData\CyberWatch\cyberwatch.updating**` (`**WatchdogPauseLock**`). Lo crea `**EjecutorTareasFirebaseService**` antes de generar los batch de **`actualizar_agente`** y **`reiniciar_servicio`**; los `.bat` lo borran en la fase de limpieza. TTL lógico **15 min** para ignorar locks huérfanos. Tras reconciliar `comando_estado=reiniciando` al arrancar, se llama a `**Eliminar()**` si aplica.
- **Watchdog en Service:** si no hay clientes en el pipe y han pasado **>60 s** desde la última actividad de conexión/desconexión, y el lock no está activo → `**LanzadorUserAgent.RelanzarDesdeTarea**` (`schtasks /Create /F` + `/Run` sobre `CyberWatch\UserAgent`). Bucle de comprobación cada **10 s**; logs con prefijo `**[Watchdog]**`.
- **Watchdog en UserAgent (`PipClientService`):** si el pipe falla de forma continuada **>30 s** y el lock no está activo → `**ServiceController.Start("CyberWatch")**` en try/catch silencioso (sin administrador suele ser acceso denegado).
- **Fuera de alcance:** hardening kernel/DACL contra terminación del proceso.

### 2.12 Logging del Service

- **Consola** (útil en depuración).
- **Archivo:** `cyberwatch_service.log` (por defecto bajo `C:\Program Files\CyberWatch\` cuando la política de permisos lo permite).
- **Firestore:** `FirestoreLoggerProvider` escribe en la colección `**cyberwatch_logs**` (servicio `CyberWatch.Service`, categoría, nivel, mensaje, máquina, hostname). Fire-and-forget para no tumbar el servicio.

### 2.13 Modo consola auxiliar

- Argumentos `**--version**`, `**-v**`, `**/version**`, `**-version**`: imprime nombre del servicio y versión leídos de `appsettings.json` y termina (útil para scripts de despliegue).

---

## 3. CyberWatch.UserAgent (WinExe, sesión de usuario)

### 3.1 Conexión al Service (Named Pipe cliente — `PipClientService`)

- Conexión cíclica al pipe `**CyberWatch_AgentPipe**`.
- Al recibir `**tipo: amenaza**`, invoca `**CapturaService.TomarCapturaAsync**` con el nombre del proceso como motivo.
- Ignora `**ping**`. Ante errores de conexión, espera **5 s** y reintenta.

### 3.2 Capturas de pantalla (`CapturaService`)

- Disparo por **pipe** (amenaza) o por comando remoto `**captura**` (desde Firestore, ver §3.5).
- Almacenamiento local bajo `**%LOCALAPPDATA%\CyberWatch\capturas\**` (evita permisos denegados en Program Files).
- Subida a **Firebase Storage** y actualización en Firestore: `**ultima_captura_url**`, `**ultima_captura_ts**`, `**ultima_captura_motivo**` (p. ej. `dashboard` o nombre de proceso).

### 3.3 Historial de navegación (`HistorialNavegacionService` + `LectorHistorialSqlite`)

- **Sincronización periódica:** cada **30 minutos**.
- Lee bases **SQLite** de historial de **Chrome**, **Edge** y **Firefox** (por perfil).
- **Filtro opcional de perfiles:** si `**Firebase:DominioEmpresa**` está configurado, puede limitar perfiles de Chrome/Edge alineados a ese dominio (comportamiento documentado en código de configuración).
- Escribe en subcolección `**historial_navegacion**` por máquina: URL, título, fecha de visita, navegador, perfil, flag de sincronizado; solo **entradas nuevas** respecto a la última sync guardada en Firestore.
- **Exportación bajo demanda:** comando `**historial_completo**` genera JSON en `**%LOCALAPPDATA%\CyberWatch\historial\`**, sube a Storage, actualiza `**ultima_historial_completo_*`** y en error `*ultima_historial_completo_error*`.

### 3.4 Ubicación GPS (`UbicacionService`)

- Usa **Windows.Devices.Geolocation** (`Geolocator`).
- Solicita permisos de ubicación; si COM falla o permisos denegados, el servicio se desactiva de forma controlada (no crashea el host).
- Escribe periódicamente en el documento de instancia: `**lat_gps`**, `**lon_gps`**, `**precision_gps**`, `**ultima_ubicacion_gps**`.

### 3.5 Comandos remotos al UserAgent (`ComandoService`)

- **Polling** cada **15 s** al documento de instancia.
- Campo `**comando_ua`:** si no está vacío, se **borra con `FieldValue.Delete`** antes de ejecutar para evitar re-ejecución al reiniciar.
- Comandos: `**captura**`, `**historial_completo**` (misma lógica que servicios dedicados).

### 3.6 Logging del UserAgent

- Consola + archivo `**cyberwatch_useragent.log**` en `**%LOCALAPPDATA%\CyberWatch\**`.
- Si Firebase está configurado: logs a `**cyberwatch_logs**` con origen `**CyberWatch.UserAgent**`.
- **Crash temprano:** si el host falla antes de inicializar logging estándar, se escribe `**%TEMP%\cyberwatch_ua_crash.txt`**.

---

## 4. CyberWatch.Shared

- **Modelos Firestore:** `InstanciaMaquina`, `Alerta`, `ServicioDesconocido`, `PuertoAbierto`, `LogAmenaza`, `AlertaSistema`, `EntradaHistorial`, etc.
- **Configuración:** `FirebaseSettings` (proyecto, bucket, credenciales, nombres de colecciones, intervalo de registro, dominio empresa).
- **Helpers:** `MachineIdHelper`, `FirestoreDbFactory`.
- **Logging:** `FileLoggerProvider`, `FirestoreLoggerProvider`.

---

## 5. Firebase — modelo de datos (resumen operativo)


| Colección / ruta                    | Función                                                                                                                                   |
| ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `cyberwatch_instancias/{machineId}` | Documento maestro de endpoint: red, geo IP, GPS, BitLocker, firewall, admins, estado `sc`, comandos, capturas, historial, alertas_sistema |
| `.../alertas`                       | Alertas de ransomware (servicio), **SecurityEventMonitor**, **servicios no-base** (`servicio_desconocido_nuevo`), **firma de servicio** (`servicio_sin_firma_valida`), **servicio anómalo** (`servicio_no_firmado`), **puertos** (`puerto_sospechoso`, `puerto_nuevo_entre_ciclos`) |
| `.../servicios_desconocidos`       | Servicios SCM fuera de whitelist base (merge por ciclo; campo `esNuevo`)                                                                  |
| `.../puertos_abiertos`              | Filas TCP IPv4 monitorizadas (`Listen` y opcionalmente `Established`; flags `esSospechoso`, `esNuevo`)                                   |
| `.../logs_amenazas`                 | Auditoría detallada de cada ciclo con detección (incluye dedup)                                                                           |
| `.../historial_navegacion`          | Visitas sincronizadas desde navegadores                                                                                                   |
| `config/ciberseguridad`             | URL (y versión) de distribución para actualización remota                                                                                 |
| `cyberwatch_logs`                   | Logs centralizados de Service y UserAgent                                                                                                 |


**Índices y reglas:** el README del repo detalla índices de **collection group** (`alertas`, `logs_amenazas`) y reglas de seguridad necesarias para el dashboard.

---

## 6. Panel / dashboard

- El README describe un **dashboard en React** (`Front/`, Vite + TypeScript) con páginas en tiempo real sobre Firestore (instancias, alertas globales con `CollectionGroup`, logs, etc.). La presencia exacta del código `Front/` depende del árbol desplegado en cada entorno.
- `**public/index.html`** en la raíz puede ser plantilla estándar de Firebase Hosting; no sustituye por sí solo al dashboard descrito en el README.

---

## 7. Herramientas y proyectos de soporte


| Proyecto                     | Rol                                                                                                                                       |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| **CyberWatch.DumpFirestore** | Utilidad de consola: vuelca colecciones configuradas a JSON/Markdown para backup o depuración (`firebase_dump.json`, `firebase_dump.md`). |
| **CyberWatch.Tests**         | Pruebas unitarias (xUnit), según lo definido en el solution.                                                                              |
| **CyberWatch.TestMalware**   | Programa de prueba para simular patrones de malware (desarrollo/validación).                                                              |
| **CyberWatch.TestEscritura** | Programa de prueba orientado a escritura de archivos (desarrollo/validación).                                                             |


---

## 8. Instalación y despliegue (alcance funcional)

- `**install.bat`:** detiene servicio y UserAgent, copia a `C:\Program Files\CyberWatch\`, registra/actualiza el servicio Windows, espera e **inicia el UserAgent** con `start`.
- **GitHub Actions / releases:** flujo documentado en `docs/DEPLOY.md` y README: build, ZIP, GitHub Releases, actualización de `config/ciberseguridad` en Firestore.
- **Credenciales:** soporte para `**CredentialPath`** o `**CredentialJson`** embebido (deploy típico sin archivo de clave en disco permanente).

---

## 9. Configuración clave (`appsettings.json`)

- `**Firebase`:** proyecto, bucket, credenciales, intervalo de registro, nombres de colecciones, `DominioEmpresa` opcional.
- `**CyberWatch`:** `Version`, `ServiceName`.
- `**Umbrales`:** umbrales numéricos, extensiones, carpetas protegidas, cuarentena, exclusiones de proceso, `MaxAmenazasPorCiclo`, `CooldownLiquidacionMinutos`, **`IntervaloServiciosMinutos`**, **`ServiciosExcluidos`**, **`SuprimirAlertasPrimerCicloServicios`** (monitor de servicios no-base), **`FirmaServiciosHabilitado`**, **`IntervaloFirmaServiciosHoras`**, **`FirmaServiciosSoloNoBase`**, **`ServiciosFirmaExcluidos`**, **`DedupFirmaServiciosHoras`** (monitor de firma Authenticode), **`MonitorServiciosAnomalosHabilitado`**, **`IntervaloServiciosAnomalosMinutos`**, **`DedupServiciosAnomalosHoras`** (monitor iteración 7 / `config/servicios`), **`IntervaloPuertosMinutos`**, **`PuertosExcluidos`**, **`SuprimirAlertasPrimerCicloPuertos`**, **`MonitorearSoloListen`** (monitor de puertos TCP), **`EntropiaHabilitada`**, **`EntropiaTamanoMuestraKb`**, **`EntropiaUmbralAlto`**, **`EntropiaBonusPuntos`**, **`ExtensionesEntropiaAltaEsperada`**, **`EntropiaRequierePatronRansomware`**, **`EntropiaMinimoPuntuacionBase`** (refuerzo opcional en `EvaluadorAmenazas`).

---

## 10. Limitaciones conocidas (comportamiento del producto)

- La correlación **proceso → archivo** depende de **ETW a nivel kernel** y resolución de PID; no equivale a un **minifilter** ni a **ETW avanzado** con correlación garantizada al 100 % en todos los escenarios.
- **Falsos positivos** pueden darse con software muy intensivo en disco; las mitigaciones están listadas en el README (exclusiones, rutas protegidas, límite por ciclo, cooldown, ámbito `Users` en disco de sistema).

---

## 11. Referencias cruzadas en el repo

- **Arquitectura y estado del proyecto:** `README.md`
- **Despliegue:** `docs/DEPLOY.md`
- **Detalle de navegación / historial:** `docs/NAVEGACION.md`
- **Iteraciones de desarrollo (alcance por hito):** `docs/README-ITERACIONES.md` y `docs/iteracion-*-pasos.md`

---

*Documento generado a partir del código y la documentación del repositorio CyberWatch. Para cambios de comportamiento, priorizar el código fuente y el README actualizado.*