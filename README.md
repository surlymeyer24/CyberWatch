# CyberWatch

Servicio Windows de detección de ransomware con sincronización en tiempo real a Firebase Firestore.
Desarrollado en C# / .NET 8.0.

---

## Arquitectura

CyberWatch se compone de dos procesos que corren en paralelo:

| Componente | Sesión | Privilegio | Rol |
|---|---|---|---|
| **CyberWatch.Service** | Session 0 | SYSTEM | Detección de amenazas, sync Firestore, geolocalización IP, servidor Named Pipe |
| **CyberWatch.UserAgent** | Sesión de usuario | Usuario normal | GPS (Windows Location API), capturas de pantalla, cliente Named Pipe, comandos remotos |

El UserAgent no tiene ventana visible (`WinExe`) y es lanzado automáticamente por el Programador de tareas al iniciar sesión (la tarea es registrada por el Service al arrancar).

### Flujo de comunicación

```
Dashboard (web) ──── Firestore ───► Service (Session 0)
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
| `CyberWatch.Dashboard` | ASP.NET Core Web | Dashboard de monitoreo (web) |
| `CyberWatch.DumpFirestore` | Console | Utilidad de backup/debug de Firestore |
| `CyberWatch.Tests` | xUnit | Pruebas unitarias |

---

## Firestore

### Colecciones

| Colección | Contenido |
|---|---|
| `cyberwatch_instancias/{machineId}` | Registro de cada máquina: hostname, IP, versión, geolocalización IP y GPS, última conexión, comando remoto |
| `alertas` | Detecciones de amenazas ransomware |
| `config/ciberseguridad` | Configuración global: versión actual y URL de descarga para actualizaciones |

### Campos de `cyberwatch_instancias`

| Campo | Origen | Descripción |
|---|---|---|
| `id`, `hostname`, `version` | Service | Identificación |
| `ip_local`, `ultima_conexion` | Service | Conectividad |
| `lat`, `lon`, `ciudad`, `pais`, `isp`, `ultima_geolocalizacion` | Service | Geolocalización por IP |
| `lat_gps`, `lon_gps`, `precision_gps`, `ultima_ubicacion_gps` | UserAgent | GPS via Windows Location API |
| `comando`, `comando_estado`, `comando_resultado` | Dashboard → Service | Comandos remotos (`actualizar_agente`, `sacar_captura`, etc.) |
| `comando_ua`, `comando_ua_estado` | Dashboard → UserAgent | Comandos al UserAgent |

### Comandos remotos disponibles

| Comando | Destino | Acción |
|---|---|---|
| `actualizar_agente` | Service | Descarga el ZIP del release, extrae, reemplaza binarios, reinicia servicio y UserAgent |
| `sacar_captura` | UserAgent | Toma screenshot y lo sube a Firebase Storage |

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
| `cw_update.log` | `%TEMP%\` | Script `.bat` de actualización remota |

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

### Bugs abiertos

- [ ] **`cyberwatch_useragent.log` no se genera** — el UserAgent corre (visible en Administrador de tareas) pero falla en silencio antes de escribir logs. Diagnosticar con `%TEMP%\cyberwatch_ua_crash.txt` (incluido desde v3.9.0).
- [ ] **`comando_ua` no desaparece de Firestore** — el UserAgent no puede conectarse a Firestore; posible causa: `CredentialJson` malformado en `appsettings.json` o `MachineIdHelper.Read()` retorna null.

### Deploy pendiente

- [ ] **v3.9.0** — incluye: fix de log path (LocalAppData), crash handler en `Program.cs`, fix `OperationCanceledException` en shutdown, fix estado "reiniciando" en Dashboard post-actualización.

### Refactoring pendiente

- [ ] Ítem 7: Dashboard monolítico (`Program.cs` > 400 líneas)
- [ ] Ítem 8: Consistencia en signed URLs de Firebase Storage
- [ ] Centralizar `LeerMachineId()` — duplicado en 7 archivos → mover a `MachineIdService` en Shared
- [ ] Crear POCO `Alerta` tipado — actualmente se usan `Dictionary<string,object>` en varios servicios
- [ ] `MonitorActividadArchivos.Eventos` sin límite de tamaño (`ConcurrentBag` sin cap)
