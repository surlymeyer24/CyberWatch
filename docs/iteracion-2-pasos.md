# Iteración 2 — Comandos remotos, UserAgent y historial de navegación

**Alcance:** Service (órdenes desde Firestore), **CyberWatch.UserAgent** (sesión de usuario), integración Storage. Consolida lo que antes estaba en los borradores “iteración 4 / 5 / 6”.

## Objetivos

1. **Ejecutor de comandos** en el Service: `actualizar_agente`, `reiniciar_servicio`, `sc_query`.
2. **Actualización OTA** desde `config/ciberseguridad` sin doble ejecución del listener.
3. **Named Pipe** Service ↔ UserAgent; capturas y GPS; comandos `comando_ua`.
4. **Historial de navegación** (Chrome / Edge / Firefox) y export `historial_completo`.

> Referencia: [FUNCIONALIDADES_CYBERWATCH.md](FUNCIONALIDADES_CYBERWATCH.md) §2.7–2.11 y §3.

---

## Parte A: Comandos remotos al Service

### 1. EjecutorTareasFirebaseService

- Listener en tiempo real sobre `cyberwatch_instancias/{machineId}` → campo `comando`.
- Estados: `comando_estado`, `comando_resultado`; mitigar doble disparo de `actualizar_agente` (lock hasta apagado tras lanzar batch).

### 2. Comandos

- **`actualizar_agente`:** descarga ZIP, batch con `Process.Start`, reinicio; post-arranque reconciliar UserAgent y tarea.
- **`reiniciar_servicio`:** net stop / net start.
- **`sc_query`:** actualizar `servicio_sc_*` en el documento de instancia.

### 3. RegistradorTareaUsuarioService

- Registrar tarea `CyberWatch\UserAgent` si existe `CyberWatch.UserAgent.exe` junto al Service.

---

## Parte B: UserAgent — pipe, capturas y GPS

### 4. AgentePipeServerService (Service)

- Pipe `CyberWatch_AgentPipe`; JSON `{"tipo":"amenaza","proceso":"..."}` y `ping` ~20 s.

### 5. PipClientService + CapturaService

- Capturas en `%LOCALAPPDATA%\CyberWatch\capturas\`; subida a Storage; `ultima_captura_*` en instancia.

### 6. UbicacionService

- GPS vía Windows Location API; degradación segura ante COM/permisos.

### 7. ComandoService (UA)

- Polling `comando_ua` ~15 s; borrado atómico del campo antes de ejecutar.

---

## Parte C: Historial de navegación

### 8. HistorialNavegacionService + LectorHistorialSqlite

- Sync cada 30 min a subcolección `historial_navegacion`; incremental.
- Opcional: `Firebase:DominioEmpresa` para filtrar perfiles.

### 9. Comando `historial_completo`

- JSON local + Storage + campos `ultima_historial_completo_*` / errores.

---

## Prueba integrada

1. Release en GitHub + Firestore → `actualizar_agente` actualiza binarios y versión sin doble stop.
2. Amenaza detectada → pipe → captura en Storage.
3. Comando remoto `captura` → misma cadena.
4. Historial: tras navegar, aparecen entradas nuevas en Firestore; `historial_completo` genera URL o error legible.

---

## Notas

- `cw_update.log`, credenciales embebidas, rutas LOCALAPPDATA: README §Bugs resueltos.
- Detalle de UI del historial: [NAVEGACION.md](NAVEGACION.md).
