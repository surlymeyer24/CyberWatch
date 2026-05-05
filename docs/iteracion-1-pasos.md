# Iteración 1 — Detección de ransomware, telemetría de instancia y respuesta activa

**Alcance:** CyberWatch.Service — núcleo del producto ya entregado en el repo. Esta iteración **consolida** lo que antes estaba repartido en los borradores “iteración 1 / 2 / 3” de la documentación.

## Objetivos

1. Monitorear actividad de archivos con **ETW** (`FileIO`), umbrales y **EvaluadorAmenazas**.
2. Persistir **alertas ransomware** y **logs_amenazas** en Firestore con **machine id** estable.
3. Centralizar modelo/config en **CyberWatch.Shared** y **registrar la instancia** periódicamente en `cyberwatch_instancias/{machineId}`.
4. **Responder** con kill, **cuarentena graduada**, directorios protegidos, cooldown y límite por ciclo.

> Referencia larga: [FUNCIONALIDADES_CYBERWATCH.md](FUNCIONALIDADES_CYBERWATCH.md) §2.1–2.7.

---

## Parte A: Monitor ETW y motor de detección

### 1. Sesión ETW `CyberWatchETW`

- Cerrar sesión previa homónima al iniciar.
- Ámbito: disco sistema solo bajo `X:\Users\`; otros volúmenes fijos: raíz del volumen.
- Eventos: creación, escritura (deduplicada por proceso/archivo por ciclo), renombrado; extensiones sospechosas desde `Umbrales:ExtensionesSospechosas`.

### 2. Exclusiones de proceso

- `Umbrales:ProcesosExcluidos`: esos procesos no alimentan el acumulador del monitor.

### 3. EvaluadorAmenazas

- Entrada: snapshot de `EventoArchivo` tras `TomarSnapshotYLimpiar()`.
- Salida: `ReporteAmenaza` con puntuación y banderas.

### 4. ServicioCyberWatch

- Intervalo `Umbrales:IntervaloTiempoSeg`.
- Mitigación `MaxAmenazasPorCiclo` (falso positivo masivo).

### 5. Firebase — alertas ransomware

- Subcolección `alertas` con deduplicación por proceso + ventana temporal.
- Subcolección `logs_amenazas` append-only por ciclo con amenaza.

---

## Parte B: Shared y registro de instancia

### 6. Proyecto CyberWatch.Shared

- Modelos (`InstanciaMaquina`, etc.), `FirebaseSettings`, `MachineIdHelper`, `FirestoreDbFactory`.

### 7. RegistroInstanciaFirebaseService

- Intervalo `Firebase:IntervaloRegistroInstanciaMinutos`.
- Merge en `cyberwatch_instancias/{machineId}`: hostname, versión, IP, geo IP (~30 min), BitLocker, firewall, admins locales, `servicio_sc_*`.

### 8. Logging centralizado

- `FirestoreLoggerProvider` → colección `cyberwatch_logs`.

---

## Parte C: Respuesta activa

### 9. Liquidación y cuarentena

- `LiquidarProcesos`; `ServicioCuarentena` mueve a `Umbrales:CarpetaCuarentena` con sufijo `.quarantine`.
- **Directorios protegidos:** sin kill ni cuarentena (`DirectoriosProtegidos`).
- **Cuarentena graduada:** primera detección en ventana → solo kill; reincidencia → cuarentena.
- **Cooldown:** `CooldownLiquidacionMinutos`.

### 10. Alertas locales

- `GestorAlertas` / `cyberwatch_service.log`.

---

## Prueba integrada

1. Patrón de escrituras/renombrados + extensión sospechosa → alerta coherente en Firestore.
2. Proceso excluido → no amenaza.
3. Snapshot limpia el acumulador cada ciclo.
4. Documento de instancia se actualiza con machine id alineado a alertas.
5. Binario en `Program Files` → alerta sin kill/cuarentena; proceso en temp con amenaza válida → kill/cuarentena según política.

---

## Notas

- Correlación proceso ↔ archivo vía ETW no equivale a minifilter (README §limitaciones).
- Lectura lazy de `machineId` para alertas: README §Bugs resueltos.
