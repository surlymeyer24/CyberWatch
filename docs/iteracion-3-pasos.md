# Iteración 3 — Eventos de Windows, servicios no base y dashboard

**Alcance:** telemetría de seguridad complementaria (Event Log), inventario de servicios SCM vs whitelist, panel web. Consolida lo que antes estaba en los borradores “iteración 7 / 8 / 9”.

## Objetivos

1. **SecurityEventMonitor:** Defender (1116), servicio Defender detenido (7036), admin agregado (4732), brute force agregado (4625).
2. **Servicios no base:** comparar SCM contra whitelist embebida; persistir y alertar **nuevos entre ciclos**.
3. **Dashboard React:** vistas globales con `CollectionGroup`, índices y reglas Firestore.

> Referencia: [FUNCIONALIDADES_CYBERWATCH.md](FUNCIONALIDADES_CYBERWATCH.md) §2.9–2.12 y §6.

---

## Parte A: SecurityEventMonitorService

### 1. Periodicidad y fuentes

- ~2 minutos si hay Firebase y `machineId`.
- Logs: Defender Operational, System, Security según Event ID.

### 2. Alertas y deduplicación

- Misma subcolección `alertas`; `Origen` = `SecurityEventMonitor`; dedup por tipo + ventana.
- Actualizar `alertas_sistema` en el documento de instancia.

---

## Parte B: Servicios desconocidos

### 3. ServiciosDesconocidosService + AnalizadorServicios

- Enumerar con `ServiceController`; **ImagePath** desde registro normalizado.
- Whitelist embebida `Data/servicios_base_windows.json`.
- Subcolección `servicios_desconocidos` con merge; **`esNuevo`** vs ciclo anterior.
- `Umbrales:IntervaloServiciosMinutos`, `ServiciosExcluidos`, `SuprimirAlertasPrimerCicloServicios`.

### 4. Alertas

- Tipo `servicio_desconocido_nuevo` cuando aplica (no en primer ciclo si está suprimido).

---

## Parte C: Dashboard e infraestructura Firebase

### 5. Front (`Front/`, Vite + TypeScript)

- Instancias, alertas globales, `logs_amenazas`, logs centralizados.

### 6. Índices y reglas

- Collection group `alertas` / `logs_amenazas` con `fechaHora`.
- Composite `alertas`: `nombreProceso` + `fechaHora` (dedup del Service).
- Desplegar `firestore.rules` acorde al panel.

---

## Prueba integrada

1. Evento 1116 (o simulación) → alerta con tipo esperado y `alertas_sistema` actualizado.
2. Servicio SCM nuevo no listado → documento + alerta en segundo ciclo (según config).
3. `firebase deploy --only firestore:indexes,firestore:rules` → dashboard sin errores de índice.

---

## Notas

- Ampliación de Event IDs: [pendientes.md](pendientes.md).
- `Front/` puede ignorarse en `.gitignore` en algunos clones.
