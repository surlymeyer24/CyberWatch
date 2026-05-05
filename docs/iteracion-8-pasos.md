# Iteración 8 — Auto-Protección / Anti-Tampering (watchdog mutuo + recuperación nativa)

**Alcance:** CyberWatch.Service, CyberWatch.UserAgent, Shared, `install.bat`, documentación. Objetivo: dificultar que un malware detenga el agente sin que el sistema u otro componente lo reactive.

**Estado:** entregada en código según este documento.

---

## Contexto

| Capa | Rol | Implementación en esta iteración |
|------|-----|-----------------------------------|
| **Recuperación nativa (SCM)** | Si el servicio cae, Windows puede reiniciarlo automáticamente | `sc failure` en `install.bat` |
| **Watchdog mutuo** | Service y UserAgent se vigilan vía Named Pipe existente | Service: sin cliente en pipe >60 s → `schtasks /Run` UserAgent; UserAgent: pipe caído >30 s → `ServiceController.Start()` (best-effort) |
| **Kernel / DACLs** | Bloquear `Process.Kill` contra el EDR | Fuera de alcance |

---

## Lock de pausa (`cyberwatch.updating`)

**Ruta:** `C:\ProgramData\CyberWatch\cyberwatch.updating` ([`WatchdogPauseLock`](../CyberWatch.Shared/Helpers/WatchdogPauseLock.cs)).

- Se crea antes de lanzar los batch de **`actualizar_agente`** y **`reiniciar_servicio`**.
- Los scripts `.bat` lo borran al finalizar la limpieza.
- Tras un reinicio post-actualización, si el documento Firestore indicaba `reiniciando`, el Service llama a `Eliminar()` por si quedó residual.
- TTL por defecto **15 minutos**: si el archivo existe pero es viejo (crash), los watchdogs lo ignoran.

---

## Parte A — Instalador

[`install.bat`](../CyberWatch.Service/install.bat): inmediatamente después de `sc create` / `sc config` y **antes** de `net start`:

```bat
sc.exe failure CyberWatch reset= 0 actions= restart/5000/restart/5000/restart/5000
```

---

## Parte B — Service (`AgentePipeServerService` + `LanzadorUserAgent`)

- [`LanzadorUserAgent`](../CyberWatch.Service/Services/LanzadorUserAgent.cs): extrae la lógica compartida `schtasks /Create /F` + `/Run` sobre la tarea `CyberWatch\UserAgent`.
- [`AgentePipeServerService`](../CyberWatch.Service/Services/AgentePipeServerService.cs): bucle cada **10 s**; si no hay clientes en el pipe y han pasado **>60 s** desde la última conexión/desconexión relevante, y el lock no está activo → relanzar UserAgent.

---

## Parte C — UserAgent (`PipClientService`)

- Tras fallos de conexión o desconexión del pipe, si pasan **>30 s** en estado fallido y el lock no está activo → `ServiceController.Start("CyberWatch")` en try/catch silencioso (sin admin suele fallar; la capa principal es SCM + Service).

---

## Parte D — Comandos remotos

[`EjecutorTareasFirebaseService`](../CyberWatch.Service/Services/EjecutorTareasFirebaseService.cs): `WatchdogPauseLock.Crear` antes de generar `cw_update.bat` / `cw_restart.bat`; los batch ejecutan `del /F /Q` del lock en la fase de limpieza. Post-actualización reconciliada: `Eliminar()`.

---

## Prueba integrada (manual)

1. **SCM:** `taskkill /F /IM CyberWatch.Service.exe` → el servicio debe volver en pocos segundos (consola `sc query CyberWatch`).
2. **Watchdog UserAgent:** matar solo `CyberWatch.UserAgent.exe` → tras ~60 s el Service relanza vía tarea programada.
3. **Watchdog Service (opcional):** con UserAgent corriendo y Service detenido de forma que el pipe falle; tras ~30 s el UA puede intentar `Start` (visible solo si el proceso tiene derechos).
4. **Lock:** disparar `actualizar_agente` o `reiniciar_servicio` desde Firestore y verificar que durante el batch no se disparan relanzamientos agresivos en log `[Watchdog]`.

---

## Documentación de producto

- [README.md](../README.md): arquitectura, Named Pipe, despliegue (`install.bat`), comandos remotos y lock.
- [FUNCIONALIDADES_CYBERWATCH.md](FUNCIONALIDADES_CYBERWATCH.md): sección Anti-Tampering.

---

## Notas

- **Coexistencia iteración 6/7:** sin cambios; el watchdog es ortogonal.
- **Producción:** conviene revisar políticas de grupo que restrinjan `sc failure` o Task Scheduler.
