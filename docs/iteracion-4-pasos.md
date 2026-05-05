# Iteración 4 — Monitor de puertos abiertos (tabla TCP del kernel)

**Estado:** implementado en **CyberWatch.Service** y modelo/config en **Shared** (sin vista nueva obligatoria en `Front/`). No equivale a un escaneo tipo Nmap: se consulta lo que **Windows ya tiene en uso** vía **IP Helper API** (`GetExtendedTcpTable`; UDP pendiente).

**Decisión:** mismo patrón operativo que `ServiciosDesconocidosService`: ciclo periódico → comparar contra ciclo anterior → persistir estado en Firestore → alertar solo cuando hay **cambio relevante** (puerto nuevo o marcado como sospechoso).

Inspiración de implementación P/Invoke: proyectos tipo **NetStat-C-Sharp** (enumerar tabla extendida y resolver PID).

---

## Contexto técnico

| Concepto | Detalle |
|----------|---------|
| Fuente de verdad | Tablas TCP/UDP del kernel expuestas por `iphlpapi.dll`, no socket scanning externo. |
| PID | Cada fila EXCEPT (`LISTEN`, `ESTABLISHED`, etc.) incluye PID del proceso dueño del socket (cuando está disponible). |
| Falsos positivos | Muchos puertos son legítimos (135, 445, 3389, puertos efímeros). Mitigar con **whitelist de puertos base**, **exclusiones por número**, y alertas solo para **lista negra** o **aparición nueva** fuera de whitelist. |

---

## Objetivos

1. Enumerar sockets TCP **Listening** y **Established** (mínimo viable: TCP IPv4; IPv6 opcional en misma iteración o siguiente).
2. Resolver **PID → nombre y ruta del proceso** (`Process.GetProcessById` + `MainModule.FileName` con manejo de acceso denegado).
3. Persistir **snapshot por máquina** en Firestore y emitir alertas cuando:
   - el puerto está en lista **sospechosa** (ej. 4444, 1337, 31337, 6667, 8080 si política lo define), o
   - el **par (protocolo, puerto local, PID)** es **nuevo respecto al ciclo anterior** y no está en whitelist base.
4. **Frontend:** vista por máquina y/o vista global filtrable por `esSospechoso`.

---

### Orden de trabajo recomendado

1. Parte A (Service + Shared + settings) hasta validar en log local sin Firebase.
2. Persistencia Firestore + deduplicación de alertas.
3. Parte B (Front) si entra en el mismo sprint.
4. Prueba integrada en VM de laboratorio.

---

## Parte A: Backend (CyberWatch.Service / Shared)

### A.1 `Detection/PuertoTcpDescriptor.cs` (nuevo)

Record o clase inmutable con campos:

- `Protocolo` (`Tcp` / `Udp` si se agrega UDP).
- `Estado` (`Listen`, `Established`, … como string o enum).
- `IpLocal`, `PuertoLocal` (ushort/int).
- `IpRemota`, `PuertoRemoto` (opcional para Established).
- `Pid` (int; 0 si no disponible).
- `NombreProceso`, `RutaProceso` (string; vacío si no se pudo resolver).

---

### A.2 `Detection/AnalizadorPuertosTcp.cs` (nuevo)

Responsabilidades:

1. **P/Invoke** a `GetExtendedTcpTable` (`AF_INET` primero). Declaraciones en clase interna estática `NativeMethods` o archivo `Interop/IpHelperApi.cs`:
   - `GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool bOrder, uint ulAf, uint TableClass, uint Reserved)`
   - Constantes: `TCP_TABLE_OWNER_PID_ALL`, familia `AF_INET` (2).
2. Parsear filas `MIB_TCPROW_OWNER_PID` (u `MIB_TCP6ROW_OWNER_PID` si IPv6).
3. Filtrar filas según política (por defecto incluir **Listen** y opcionalmente **Established** para reducir ruido — documentar decisión en código).
4. Para cada PID válido, resolver proceso:
   ```csharp
   try { using var p = Process.GetProcessById(pid); ... }
   catch { /* sin elevación puede fallar para procesos ajenos */ }
   ```
5. Método público `EnumerarActivos()` → `IReadOnlyList<PuertoTcpDescriptor>`.

**Notas:** ejecutar como SYSTEM en el Service ayuda a resolver más procesos; aun así algunos PID pueden fallar → log Debug y continuar.

---

### A.3 Listas embebidas — JSON como recurso

Crear en `CyberWatch.Service/Data/`:

| Archivo | Contenido |
|---------|-----------|
| `puertos_base_windows.json` | Array de enteros: puertos que **no** deben solos disparar alerta “nuevo” en entornos típicos (ej. 135, 139, 445, 3389, 5985 — ajustar por SKU). |
| `puertos_sospechosos.json` | Array de enteros: si aparece **Listening** en cualquier interfaz, marcar `esSospechoso` y priorizar alerta (4444, 1337, 31337, 6667, 6660, 9001, 8080 según política del equipo). |

En `.csproj` del Service: `<EmbeddedResource Include="Data\puertos_*.json" />` (mismo patrón que `servicios_base_windows.json`).

### A.4 `Services/WhitelistPuertosBase.cs` (nuevo)

- `Cargar()` → `HashSet<int>` desde `puertos_base_windows.json`.
- Función pura `bool EsPuertoBase(int puerto, HashSet<int> whitelist)`.

---

### A.5 `Detection/PuertoEvaluado.cs` (nuevo)

Propiedades:

- `Descriptor` (`PuertoTcpDescriptor`).
- `EsSospechoso` (bool) — true si `PuertoLocal` ∈ lista negra.
- `EsNuevoEntreCiclos` (bool) — true si no estaba en el conjunto del ciclo anterior (clave sugerida: `"Listen:{puerto}:{pid}"` o solo puerto+PID para Listen).

**Mitigación primer arranque:** igual que servicios: opción `SuprimirAlertasPrimerCicloPuertos` para no inundar al arrancar con todo el mapa actual.

---

### A.6 `Services/PuertosAbiertosMonitorService.cs` (nuevo, `BackgroundService`)

Copiar estructura de [ServiciosDesconocidosService.cs](../CyberWatch.Service/Services/ServiciosDesconocidosService.cs):

- `ExecuteAsync`: si no Windows o sin `machineId`, salir con log.
- Conectar Firestore si `FirebaseSettings.IsAdminConfigured`.
- Bucle `await Task.Delay(intervalo)` donde `intervalo = TimeSpan.FromMinutes(Umbrales:IntervaloPuertosMinutos)` (default **5**).

Por cada ciclo:

1. `var actual = AnalizadorPuertosTcp.EnumerarActivos()`.
2. Evaluar cada fila → `PuertoEvaluado` usando `_puertosCicloAnterior` (HashSet de claves).
3. Para cada ítem: **upsert** documento en subcolección (ver A.8).
4. Si `(EsSospechoso || EsNuevoEntreCiclos)` y pasa filtros (`PuertosExcluidos`, suprimir primer ciclo), llamar **PersistirAlerta** con dedup 10 min (mismo patrón `WhereEqualTo` + `fechaHora` que servicios desconocidos).
5. Actualizar `_puertosCicloAnterior` con el conjunto de claves del ciclo actual.

---

### A.7 `CyberWatch.Shared/Config/FirebaseSettings.cs`

Agregar propiedad:

```csharp
/// <summary>Subcolección por máquina: sockets TCP/UDP monitoreados (upsert por ciclo).</summary>
public string FirestoreCollectionPuertosAbiertos { get; set; } = "puertos_abiertos";
```

### A.8 `CyberWatch.Shared/Models/PuertoAbiertoFirestore.cs` (nuevo nombre al gusto)

Campos sugeridos para el documento (ID del doc ver A.9):

- `protocolo`, `puertoLocal`, `estado`, `pid`, `nombreProceso`, `rutaProceso`, `ipLocal`
- `machineId`, `hostname`, `fechaDeteccion` (Timestamp)
- `esSospechoso`, `esNuevoEntreCiclos`

### A.9 ID de documento Firestore

Evitar caracteres `/` en IDs. Sugerencia:

- `tcp_listen_{puerto}_{pid}` para filas Listen; si colisión, añadir hash corto de IP local.

Implementar helper `IdDocumentoFirestore(PuertoTcpDescriptor)` en el mismo servicio (análogo a `ServiciosDesconocidosService.IdDocumentoFirestore`).

---

### A.10 `CyberWatch.Service/Config/UmbralesSettings.cs`

Nuevas propiedades:

| Propiedad | Default | Descripción |
|-----------|---------|-------------|
| `IntervaloPuertosMinutos` | 5 | Intervalo entre ejecuciones del monitor. |
| `PuertosExcluidos` | `[]` | Enteros a ignorar para alertas “nuevo” (software interno). |
| `SuprimirAlertasPrimerCicloPuertos` | `true` | Primer ciclo: persistir docs, no crear alertas en `alertas`. |
| `MonitorearSoloListen` | `true` | Si true, no alertar por Established (reduce ruido). |

### A.11 `CyberWatch.Service/appsettings.json`

Añadir sección bajo `Umbrales` las claves anteriores con valores por defecto.

### A.12 `CyberWatch.Service/Program.cs`

- `services.AddHostedService<PuertosAbiertosMonitorService>();`

### A.13 Modelo `Alerta` / tipo de alerta

- Reutilizar modelo existente [Alerta.cs](../CyberWatch.Shared/Models/Alerta.cs): nuevo valor de **`Tipo`** coherente con los existentes (string), por ejemplo `puerto_sospechoso` o `puerto_nuevo_entre_ciclos`.
- `Descripcion` / `Detalle`: incluir puerto, PID, proceso, ruta, IP local.

---

## Parte B: Frontend (`Front/`)

### B.1 Ruta y datos

- Nueva ruta `/puertos` o panel dentro del detalle de instancia.
- Leer subcolección `puertos_abiertos` con `onSnapshot` o consulta puntual.
- Columnas: puerto, estado, PID, proceso, ruta, badge **Sospechoso** si `esSospechoso`.

### B.2 UX

- Filtro “solo sospechosos”.
- Enlace al proceso si en el futuro hay vista de proceso (opcional; YAGNI).

### B.3 Calidad

- `npm run build` sin errores.

---

## Prueba integrada

1. **Local sin Firestore:** iniciar Service con logging Debug → log muestra recuento de filas TCP y resolución de PID para el proceso de prueba.
2. **Laboratorio:** abrir listener en puerto **4444** (`Test-NetConnection` o herramienta mínima) → tras un ciclo debe aparecer documento con `esSospechoso == true` y alerta en `alertas` (si no es primer ciclo suprimido).
3. **Puerto base** (ej. 135): aparece en subcolección pero **no** alerta como “nuevo” si está en whitelist base.
4. **Sin machineId:** monitor no escribe en Firestore (mismo patrón que otros hosted services).

---

## Fuera de alcance (explícito)

- Escaneo activo de red / sinopsis tipo Nmap.
- Bloqueo de firewall o cierre de sockets (solo **visibilidad** y alerta).
- Correlación multi-máquina / C2 (iteración futura; ver [pendientes.md](pendientes.md)).

---

## Notas

- Si el volumen de filas Established es alto, mantener `MonitorearSoloListen=true` por defecto.
- IPv6 y UDP: documentar como seguimiento rápido si hace falta paridad con `netstat -ano`.
