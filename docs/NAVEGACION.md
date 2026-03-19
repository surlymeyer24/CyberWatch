# Historial de Navegación — CyberWatch

Documentación técnica del módulo de sincronización de historial de navegación.

---

## Resumen

El `HistorialNavegacionService` (UserAgent) lee el historial local de Chrome, Edge y Firefox, y lo sincroniza a Firestore cada 30 minutos. Solo sube entradas nuevas desde la última sincronización.

---

## Arquitectura

```
Navegadores (SQLite local)
    │
    ├── Chrome:  %LocalAppData%\Google\Chrome\User Data\{perfil}\History
    ├── Edge:    %LocalAppData%\Microsoft\Edge\User Data\{perfil}\History
    └── Firefox: %AppData%\Mozilla\Firefox\Profiles\{perfil}\places.sqlite
    │
    ▼
LectorHistorialSqlite (copia temporal + lectura ReadOnly)
    │
    ▼
HistorialNavegacionService (cada 30 min)
    │
    ▼
Firestore: cyberwatch_instancias/{machineId}/historial_navegacion/{auto-id}
```

---

## Archivos involucrados

| Archivo | Rol |
|---|---|
| `CyberWatch.UserAgent/services/HistorialNavegacionService.cs` | BackgroundService: ciclo de sync cada 30 min, batch writes a Firestore |
| `CyberWatch.UserAgent/services/LectorHistorialSqlite.cs` | Lectura de SQLite de Chrome, Edge y Firefox. Manejo de timestamps WebKit/PRTime |
| `CyberWatch.Shared/Models/EntradaHistorial.cs` | Modelo Firestore con atributos `[FirestoreData]` / `[FirestoreProperty]` |

---

## Flujo detallado

### 1. Arranque del UserAgent

Cuando `HistorialNavegacionService` inicia, llama a `ObtenerUltimaSyncAsync()`:

```
¿Existe "ultima_sync_historial" en Firestore para esta máquina?
   ├── SÍ → usa esa fecha (ej: 2026-03-16 14:30:00 UTC)
   └── NO (primera vez) → usa DateTime.UtcNow (desde ahora, sin historial previo)
```

Ese valor se guarda en `_ultimaSync` en memoria.

### 2. Ciclo cada 30 minutos

Cada 30 minutos ejecuta `SincronizarHistorialAsync()`:

**Paso A — Leer historial local de los navegadores:**

Para cada navegador, el `LectorHistorialSqlite`:

1. Descubre perfiles automáticamente:
   - Chromium: carpetas `Default`, `Profile 1`, `Profile 2`, etc.
   - Firefox: todas las carpetas dentro de `Profiles/`
2. Copia el archivo `.db` y el `.wal` (Write-Ahead Log) a `%LocalAppData%\CyberWatch\temp\`
   - La copia es necesaria porque el navegador tiene el archivo bloqueado mientras corre
   - El `.wal` contiene las escrituras más recientes que aún no se volcaron al `.db`
3. Abre la copia en modo **ReadOnly**
4. Consulta solo visitas con `fecha > _ultimaSync`
5. Convierte timestamps del formato nativo al `DateTime` UTC:
   - **Chrome/Edge (WebKit):** microsegundos desde 1601-01-01 UTC
   - **Firefox (PRTime):** microsegundos desde Unix epoch (1970-01-01)
6. Elimina los archivos temporales

**Paso B — Subir entradas nuevas a Firestore:**

Si hay entradas nuevas, las sube como documentos a la subcollección:

```
cyberwatch_instancias/{machineId}/historial_navegacion/{auto-id}
```

Cada documento contiene:

| Campo | Tipo | Ejemplo |
|---|---|---|
| `url` | string | `"https://google.com"` |
| `titulo` | string (nullable) | `"Google"` |
| `fecha_visita` | Timestamp | `2026-03-18T09:30:00Z` |
| `navegador` | string | `"chrome"`, `"edge"`, `"firefox"` |
| `perfil` | string (nullable) | `"Default"`, `"Profile 1"`, `"abc123.default-release"` |
| `sincronizado` | Timestamp | `2026-03-18T10:00:00Z` |

Usa **batch writes** de máximo 450 documentos por batch (Firestore limita a 500 operaciones por batch).

**Paso C — Actualizar el marcador de sincronización:**

1. Toma la `fecha_visita` más reciente del batch
2. La escribe en `cyberwatch_instancias/{machineId}.ultima_sync_historial` (con `SetOptions.MergeFields`)
3. Actualiza `_ultimaSync` en memoria para el próximo ciclo

---

## Navegadores soportados

| Navegador | Base de datos | Formato timestamp | Tabla de URLs | Tabla de visitas |
|---|---|---|---|---|
| Chrome | `History` (SQLite) | WebKit (μs desde 1601-01-01) | `urls` | `visits` |
| Edge | `History` (SQLite) | WebKit (μs desde 1601-01-01) | `urls` | `visits` |
| Firefox | `places.sqlite` | PRTime (μs desde Unix epoch) | `moz_places` | `moz_historyvisits` |

> **Nota:** Chrome y Edge usan el mismo formato (Chromium). Brave y Opera también usan Chromium pero no están implementados actualmente.

---

## Retención de historial por navegador

La cantidad de historial disponible depende de la configuración de cada navegador:

- **Chrome/Edge:** ~90 días por defecto (configurable por el usuario)
- **Firefox:** basado en límite de espacio, generalmente varios meses

---

## Deduplicación

No hay deduplicación explícita de URLs. Cada visita es un documento independiente (una misma URL visitada 10 veces genera 10 documentos con distintas `fecha_visita`). Esto refleja el comportamiento real de navegación.

La deduplicación temporal se logra con `_ultimaSync`: solo se suben visitas con fecha posterior a la última sincronización, evitando duplicados entre ciclos.

---

## Comando remoto: `historial_completo` (pendiente)

Funcionalidad planificada para permitir al Dashboard disparar una sincronización completa del historial (desde `DateTime.MinValue` en vez de `_ultimaSync`), trayendo todo el historial almacenado en los navegadores.

Flujo:
1. Dashboard escribe `comando_ua: "historial_completo"` en Firestore
2. `ComandoService` del UserAgent lo detecta
3. Llama a `HistorialNavegacionService` con `despuesDe = DateTime.MinValue`
4. Se reporta resultado en Firestore

---

## Campos en `cyberwatch_instancias`

| Campo | Descripción |
|---|---|
| `ultima_sync_historial` | Timestamp de la fecha de visita más reciente sincronizada. Usado como punto de partida del próximo ciclo |

---

## Logs

Prefijo: `[Historial]`

| Mensaje | Significado |
|---|---|
| `Iniciado. Última sync: {fecha}` | Servicio arrancó, muestra desde cuándo va a buscar |
| `{Navegador}/{Perfil}: {N} entradas nuevas` | Entradas leídas de un perfil específico |
| `Sin entradas nuevas.` | Ningún navegador tenía visitas nuevas desde `_ultimaSync` |
| `Sincronizadas {N} entradas a Firestore.` | Batch write exitoso |
| `{Navegador} no encontrado en {Path}` | El navegador no está instalado (debug, no es error) |
| `Error leyendo {Navegador}/{Perfil}` | Falló la lectura de un perfil (warning, no detiene el ciclo) |
