# Iteración 5 — Entropía en la puntuación de amenaza y lista de puertos remota (Firebase)

**Alcance:** CyberWatch.Service (+ Shared + Firebase). Dos ejes que pueden implementarse en **un mismo sprint** o partir en **5A** (entropía) y **5B** (config remota de puertos), según capacidad.

**Estado:** entregada en código (Service + Shared). Mantener README/FUNCIONALIDADES alineados ante cambios.

---

## Contexto y decisiones de dominio

### Entropía y falsos positivos

La **entropía de Shannon** (normalizada 0–8 bits por byte) mide qué tan “aleatorios” parecen los datos de un archivo. Referencias típicas:

| Tipo de contenido | Entropía aproximada |
|-------------------|---------------------|
| Texto plano, código, muchos binarios estructurados | Baja–media |
| Datos cifrados, muchos comprimidos (ZIP), JPEG/MP4, ficheros ya aleatorizados | Alta (cerca del máximo 8) |

**Regla de negocio:** la entropía **no** debe usarse sola para bloquear ni para disparar cuarentena. Sirve como **refuerzo** dentro del modelo ya existente de **puntuación** (`EvaluadorAmenazas`) y **umbrales**.

**Combinación obligatoria con señales existentes:**

- Solo suma a la puntuación (o solo activa una rama adicional) cuando ya hay **patrón de comportamiento** coherente con ransomware en el ciclo: muchas escrituras y/o renombrados, extensiones sospechosas, etc., según lo ya definido en `Umbrales`.
- Procesos en `ProcesosExcluidos` siguen sin ser evaluados como amenaza salvo política explícita futura.

**Filtro por tipo de archivo / extensión (mitigar ZIP, JPG, MP4):**

- Es **esperable** alta entropía al **guardar** un `.zip`, `.jpg`, `.png`, `.mp4`, etc. No penalizar solo por extensión final si el proceso es legítimo y ya está excluido o el volumen de eventos no supera umbrales.
- Indicador más fuerte de ransomware (fase avanzada): un proceso que **sobrescribe** un archivo de perfil previo distinto (p. ej. documento Office `.docx` — entropía moderada en muestra inicial) dejando contenido de **entropía muy alta** en el mismo path. Eso implica **correlación temporal** lectura/muestra inicial → escritura final (diseño posible en sub-iteración; ver “Fuera de alcance inicial”).

### Lista blanca de puertos en Firebase (`config/red`)

**Motivo:** escalabilidad operativa. Con muchos endpoints, cambiar `appsettings.json` o redesplegar por cada nuevo software corporativo no escala.

**Decisión:** mantener JSON embebido (`Data/puertos_*.json`) como **fallback** si no hay documento o hay error de red, y aplicar la política definida en **`config/red`** cuando exista.

#### Contrato JSON oficial (colección `config`, ID `red`)

Ejemplo canónico guardado en el repo como referencia: [config-red.example.json](config-red.example.json).

```json
{
  "puertos_globales_permitidos": [80, 443, 3389, 1433],
  "procesos_red_excluidos": ["chrome.exe", "sqlservr.exe", "CyberWatch.Service.exe"],
  "bloqueo_estricto_activo": false,
  "ultima_modificacion": "2026-05-05T14:45:20Z"
}
```

| Campo | Tipo | Uso |
|-------|------|-----|
| `puertos_globales_permitidos` | `array<int>` | Puertos de escucha considerados **legítimos** en toda la organización (ej. HTTP 80/443, RDP 3389, SQL 1433). Equivalente operativo a la whitelist base remota: un socket **Listen** en uno de estos puertos **no** debe disparar alerta por “nuevo entre ciclos” (según regla de merge acordada). |
| `procesos_red_excluidos` | `array<string>` | Nombres de proceso **confiables** que pueden abrir puertos (incl. efímeros) sin generar alertas de ruido. Comparación recomendada sin distinguir mayúsculas y aceptando nombre con o sin `.exe` (misma convención que `ProcesosExcluidos`). Si el **PID** del socket coincide con uno de estos procesos, omitir o degradar alertas según política. |
| `bloqueo_estricto_activo` | `bool` | Reservado para **futuro**: si en algún release se habilita respuesta activa ante sockets “no autorizados”, este flag activaría ese comportamiento (p. ej. terminar proceso). **Iteración 5 inicial:** solo persistir en caché y loguear; **sin** matar procesos. |
| `ultima_modificacion` | `string` (ISO 8601 UTC) | Auditoría humana; opcional parsear en el Service para logs (“config red aplicada, última modificación …”). |

**Lista negra / sospechosos:** el JSON anterior **no** incluye puertos sospechosos remotos. Hasta ampliar el esquema, la lista **`puertos_sospechosos.json`** embebida sigue aplicándose para marcar `puerto_sospechoso`. Evolución opcional: añadir en una revisión del mismo documento un campo `puertos_globales_sospechosos` (o documento hermano).

**Suscripción en tiempo real:** al arrancar el Service, obtener un snapshot inicial (`GetSnapshotAsync`) y suscribirse al documento con **`Listen`** sobre la `DocumentReference` (`Google.Cloud.Firestore`), igual que el patrón de tiempo real ya usado para comandos en instancia. Cada callback actualiza la **caché en memoria** (thread-safe); el siguiente ciclo de `PuertosAbiertosMonitorService` usa ya los valores nuevos **sin reinicio** del servicio Windows.

**Reglas Firestore:** lectura con cuenta de servicio del agente (Admin SDK); escritura desde consola / panel / función solo roles autorizados.

---

## Objetivos

1. **Entropía:** calcular entropía de **muestra** de archivos relevantes en el flujo ETW y usarla solo como **bonus** de puntuación junto con escrituras/renombrados/proceso no excluido; listas de extensiones de “alta entropía esperada” para no inflar falsos positivos.
2. **Puertos:** cargar `puertos_globales_permitidos` y `procesos_red_excluidos` desde **`config/red`**, con **merge** frente a valores embebidos; refresco en vivo con **`Listen`** en el documento; respetar `bloqueo_estricto_activo` solo como flag futuro hasta definir respuesta activa.
3. Documentar índices/reglas si el dashboard debe editar ese documento (solo personal autorizado).

---

### Orden de trabajo recomendado

1. Definir contrato exacto del documento `config/...` y reglas de seguridad.
2. Implementar caché + listener en Service; adaptar `WhitelistPuertosBase` / `PuertosAbiertosMonitorService` para leer **primero** caché remota.
3. Implementar `CalculadorEntropia` + integración en `EvaluadorAmenazas` con flags en `Umbrales`.
4. Pruebas unitarias (entropía conocida: texto vs bytes aleatorios; merge de listas).
5. Actualizar README y FUNCIONALIDADES.

---

## Parte A: Entropía en detección ransomware

### A.1 `Response/CalculadorEntropia.cs` o `Detection/CalculadorEntropiaShannon.cs`

- Entrada: stream o primeros **N** KB/MB del archivo (configurable `Umbrales:EntropiaTamanoMuestraKb`).
- Salida: double 0–8 (o float); manejar archivo bloqueado / inexistente sin tumbar el ciclo.

### A.2 `UmbralesSettings` — nuevas claves sugeridas

| Clave | Rol |
|-------|-----|
| `EntropiaHabilitada` | bool, default false hasta validar en pilotaje |
| `EntropiaTamanoMuestraKb` | tamaño máximo de lectura |
| `EntropiaUmbralAlto` | umbral a partir del cual suma puntos (ej. 7.0–7.5) |
| `EntropiaBonusPuntos` | puntos a sumar al score si aplica (ej. +1 o +2) |
| `ExtensionesEntropiaAltaEsperada` | `.zip`, `.jpg`, `.png`, `.mp4`, … — no aplicar bonus si solo esto + proceso ya cubierto por exclusiones |
| `EntropiaRequierePatronRansomware` | bool true — solo bonus si ya hay escrituras/renombrados por encima de cierto umbral en el ciclo |

### A.3 `EvaluadorAmenazas.cs`

- Tras evaluar escrituras/renombrados/extensiones, **opcionalmente** muestrear archivo(s) representativos del proceso en el ciclo (política: último archivo escrito con extensión no en “alta entropía esperada”, o top-K paths).
- Si `EntropiaRequierePatronRansomware`: solo sumar bonus si el score base ya ≥ umbral intermedio o hay combinación escrituras+renombrados como hoy.
- **Nunca** usar solo entropía para superar `UmbralPuntuacionAmenaza` sin al menos una señal de comportamiento del modelo actual (documentar invariante).

### A.4 `ReporteAmenaza` / alertas Firestore

- Campos opcionales: `entropiaMuestra`, `entropiaAplicadaComoBonus` (bool), para auditoría en `logs_amenazas` / alerta.

### A.5 Correlación “.docx → sobrescritura de altísima entropía” (opcional / fase 2)

- Requiere estado por `(proceso, ruta)` con muestra **antes** de la ráfaga de escrituras — mayor complejidad y riesgo de FPs con editores legítimos.
- Documentar como **fuera de alcance** de la primera entrega de la iteración 5 o como **iteración 5.1**.

---

## Parte B: Config remota de puertos (`config/red`)

### B.1 Firestore — ruta del documento

- Colección: **`config`** · Documento: **`red`** → ruta completa `config/red`.
- Crear el documento según [config-red.example.json](config-red.example.json).
- Reglas: escritura solo operadores / backend autorizado; lectura según modelo de seguridad del proyecto (Admin SDK del Service ignora reglas de cliente).

### B.2 Modelo C# (Shared) — `ConfigRedDocument` (nombre tentativo)

Propiedades alineadas al JSON (atributos `[FirestoreProperty]` con nombres snake_case iguales al documento):

- `PuertosGlobalesPermitidos` → `puertos_globales_permitidos`
- `ProcesosRedExcluidos` → `procesos_red_excluidos`
- `BloqueoEstrictoActivo` → `bloqueo_estricto_activo`
- `UltimaModificacion` → `ultima_modificacion` (`string` ISO o parseo a `DateTime`)

### B.3 `FirebaseSettings` (Shared)

- Opcional: `ConfigRedDocumentPath` = `"config/red"` por defecto (colección + id).

### B.4 Servicio `Services/ConfigRedFirestoreService` (HostedService o singleton iniciado desde `Program`)

1. Tras crear `FirestoreDb`, resolver referencia: `_db.Collection("config").Document("red")` (o usar settings).
2. **Snapshot inicial:** `await docRef.GetSnapshotAsync()` → deserializar a `ConfigRedDocument` si `Exists`.
3. **Suscripción:** `docRef.Listen(metadataChanges: MetadataChanges.Include, listener)` — en cada cambio actualizar cachía thread-safe (`volátil` + `Interlocked` o `ReaderWriterLockSlim`).
4. Exponer al monitor:
   - `IReadOnlySet<int> PuertosGlobalesPermitidos`
   - `HashSet<string> ProcesosRedExcluidos` (comparador OrdinalIgnoreCase)
   - `bool BloqueoEstrictoActivo` (solo lectura para futuro)
   - `string? UltimaModificacionRaw`
5. Si el documento **no existe** o falla el parseo: log Warning y usar **solo** embebidos (`WhitelistPuertosBase`).

### B.5 `PuertosAbiertosMonitorService` — reglas de aplicación

1. **Whitelist de puertos:** para decidir “¿puerto base?” usar **unión** `puertos_globales_permitidos` ∪ `puertos_base_windows.json` (recomendado: no perder defaults offline) **o** política “remoto reemplaza embebido si el doc existe” — documentar la elegida en código.
2. **Procesos:** antes de emitir alerta `puerto_nuevo_entre_ciclos` o `puerto_sospechoso`, si `NombreProceso` del descriptor está en `procesos_red_excluidos`, **no alertar** (o solo persistir en `puertos_abiertos` sin `alertas`, según política).
3. **`bloqueo_estricto_activo`:** no implementar kill en iteración 5 inicial; si `true`, opcional log `Information` para ensayo operativo.

### B.6 Observabilidad

- Log al recibir snapshot: cantidad de puertos permitidos remotos, cantidad de procesos excluidos, `ultima_modificacion`, valor de `bloqueo_estricto_activo`.

---

## Parte C: Frontend / operación (opcional)

- Panel mínimo o sección en dashboard para editar arrays con validación (solo si ya tenéis auth); si no, documentar edición manual en Consola Firebase.

---

## Prueba integrada

### Entropía

1. Archivo de texto grande → entropía baja; no debe sumar bonus que dispare amenaza sin patrón de escrituras.
2. Archivo de bytes aleatorios / cifrado simulado → entropía alta; sin ráfaga de escrituras en ETW, **no** alerta solo por entropía.
3. Simulación `CyberWatch.TestMalware` o script: patrón de escrituras + archivo de alta entropía → score aumenta solo según reglas.

### Puertos remotos

1. Crear `config/red` con `puertos_globales_permitidos` que incluya **8080**; verificar que una PC con Listen en 8080 no genere alerta “nuevo entre ciclos” (según merge con embebidos).
2. Añadir `explorer.exe` o un proceso de prueba a `procesos_red_excluidos` y comprobar que desaparecen alertas ruidosas para ese PID/nombre.
3. Borrar el documento o vaciar arrays → el listener recibe snapshot vacío/inexistente y el Service cae a whitelist embebida.
4. Editar `ultima_modificacion` desde consola y confirmar log en el Service al aplicar snapshot.

---

## Fuera de alcance (explícito)

- Correlación fina docx→cifrado sin diseño de estado por archivo (salvo sub-iteración acordada).
- Entropía en cuarentena post-movimiento (eso enlaza con iteración de cuarentena “pro”; puede referenciarse en docs futuros).
- IPv6/UDP en monitor de puertos.

---

## Notas

- Pilotaje con `EntropiaHabilitada=false` en producción hasta medir FP.
- Versionado del esquema del documento `config/red` si en el futuro agregáis regiones, `puertos_globales_sospechosos`, o listas por hostname.
