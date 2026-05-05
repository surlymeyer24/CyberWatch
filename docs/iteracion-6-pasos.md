# Iteración 6 — Firma digital (Authenticode) en servicios Windows

**Alcance:** CyberWatch.Service (+ Shared + Firebase). Complementa la iteración de **servicios no-base** (`ServiciosDesconocidosService`) con una señal distinta: **integridad y autenticidad del binario** frente a **suplantación por nombre** (`svchost.exe` falso, etc.).

**Estado:** entregada en código (Service + Shared). Mantener README/FUNCIONALIDADES alineados ante cambios.

---

## Contexto y decisiones de dominio

### Qué es la firma digital en ejecutables (.exe, .dll, .sys)

En binarios Windows, la firma **Authenticode** actúa como un **sello criptográfico** sobre el contenido del archivo:

| Garantía | Significado operativo |
|----------|------------------------|
| **Autenticidad** | Indica **quién** firmó (sujeto del certificado, p. ej. publicador “Microsoft Corporation”). |
| **Integridad** | Si se altera **un solo byte** del ejecutable respecto al momento de la firma, la firma deja de validarse (el “sello” se rompe). |

**Por qué importa al EDR:** un atacante puede renombrar malware como `svchost.exe` o copiar el ícono; **no** puede generar una firma válida emitida por Microsoft sin la clave privada corporativa. Comparar solo **nombre SCM** o **ImagePath** sin firma abre **falsos negativos**.

### Relación con lo ya implementado

- **`ServiciosDesconocidosService` / `AnalizadorServicios`:** detecta servicios cuyo **nombre corto SCM** no está en whitelist **estática** embebida. Eso **no** comprueba si el `.exe` en disco está firmado ni por quién.
- **Iteración 6:** añade una capa **por archivo**: para cada servicio candidato (p. ej. todos los **Running**, o solo los ya marcados como no-base — decisión de producto), resolver la ruta del ejecutable y evaluar **Authenticode** / cadena de confianza.

**Reglas de negocio sugeridas (configurables en `Umbrales`):**

- No sustituir la lógica de whitelist de servicios; **combinar** (ej.: alerta distinta `servicio_sin_firma_valida` vs `servicio_desconocido_nuevo`).
- Tratar **sin firma** o **firma inválida** / **no confiable** como **señal de severidad alta** en alerta (texto + tipo dedicado).
- Respetar exclusiones existentes donde tenga sentido (`ServiciosExcluidos`, o lista nueva `ServiciosFirmaExcluidos` si hace falta software interno sin firma).

---

## Objetivos

1. **Enumerar** servicios en ejecución (o el subconjunto acordado) y obtener **ruta real del ejecutable** (WMI `Win32_Service.PathName` ya alineado con la normalización que usa el proyecto para ImagePath).
2. **Validar firma** del binario con una primera implementación **mantenible** (API .NET) y camino de evolución documentado hacia **WinVerifyTrust** / librería tipo **AuthenticodeCheck** (MIT) para revocación y paridad con productos comerciales.
3. **Persistir** alertas en Firestore (`alertas` bajo `cyberwatch_instancias/{machineId}`) con tipo estable y deduplicación razonable; opcional subcolección de **estado** si se quiere historial por servicio.
4. **Configuración:** intervalo de escaneo, toggles, listas de exclusión, umbral de ruido (no alertar dos veces por hora el mismo servicio/ruta).

---

## Parte A: Dos caminos de implementación

### A.1 Camino rápido — `System.Security.Cryptography.X509Certificates`

- Idea: cargar el certificado del firmante desde el PE y comprobar cadena en el almacén del sistema (`X509Certificate2`, verificación de cadena).
- **APIs a evaluar en implementación real:** según versión de .NET, puede usarse lectura del certificado de firma del ejecutable (p. ej. APIs específicas para **signed file** frente a `CreateFromCertFile`, pensada sobre todo para `.cer`). La iteración en código debe validar en Windows 10/11 el método exacto que extrae **Authenticode del PE**, no solo un certificado suelto.
- **Límite:** no equivale por sí solo a **WinVerifyTrust** completo (políticas de confianza del SO, revocación OCSP/CRL en el mismo nivel que el shell de Windows). Suficiente para MVP interno si se documentan los límites.

### A.2 Camino pro — `WinVerifyTrust` / librería consolidada

- **WinVerifyTrust:** API no administrada; validación alineada con lo que usa el sistema para “¿este ejecutable está firmado y es de confianza?”.
- **Alternativa práctica:** empaquetado existente (p. ej. **AuthenticodeCheck**, licencia MIT) para reducir superficie de P/Invoke y errores de estructuras.
- **Objetivo de producto:** mismas alertas que en A.1 pero con **menos falsos** en escenarios de certificados revocados o cadenas rotas.

**Decisión de entrega:** implementar **A.1** primero detrás de una interfaz `IValidadorFirmaEjecutable`, dejar **A.2** como intercambio comentado o segunda PR.

---

## Parte B: Servicio en segundo plano (`BackgroundService`)

### B.1 Nombre y ubicación sugeridos

- Clase: `MonitorServiciosFirmaDigitalService` o `ServiciosFirmaDigitalMonitorService` (evitar colisión semántica con `ServiciosDesconocidosService`).
- Namespace: `CyberWatch.Service.Services` (consistente con otros hosted services).

### B.2 Flujo por ciclo

1. Si no hay Firebase / `machineId`, solo log local (igual que otros monitores).
2. Consulta WMI: servicios con `State = 'Running'` (o filtro acordado).
3. Por cada fila: `PathName` → **normalizar** a ruta de ejecutable (comillas, argumentos, variables de entorno — reutilizar helper si existe en `AnalizadorServicios` / registro).
4. Si el archivo no existe o no es legible → tratar como error suave + log (opcional alerta “ruta inválida”).
5. `IValidadorFirmaEjecutable.Validar(ruta)` → si **no confiable** / **sin firma** → construir alerta y enviar vía `IFirebaseAlertService` o el mismo patrón que `ServiciosDesconocidosService`.
6. `Task.Delay(intervalo)` — intervalo **largo** por defecto (p. ej. **1 hora**) para no martillar disco ni WMI.

### B.3 Borrador de referencia (adaptar al repo)

El siguiente fragmento es **orientativo**; al implementar hay que sustituir `Console.WriteLine` por logger + Firestore, unificar normalización de rutas con el código existente y corregir la API .NET concreta para **firma en PE**:

```csharp
// Pseudocódigo / borrador — no copiar literal sin revisar APIs de firma para PE
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var intervalo = TimeSpan.FromHours(1);
    while (!stoppingToken.IsCancellationRequested)
    {
        await AnalizarServiciosAsync(stoppingToken);
        await Task.Delay(intervalo, stoppingToken);
    }
}
```

---

## Parte C: Firestore y modelo de alerta

### C.1 Tipo de alerta sugerido

- **`tipo`:** `servicio_sin_firma_valida` (o `servicio_firma_anomala`) — nombre estable para queries y dashboard.
- **Campos útiles:** `nombreServicio` (SCM), `rutaEjecutable` / `detalle`, `descripcion` legible, `fechaHora`, `machineId`, `hostname`, opcional `subjectCertificado`, `razonFallo` (sin_firma | cadena_invalida | revocado | …).

### C.2 Dedup

- Evitar spam: misma tupla `(machineId, nombreServicio, rutaNormalizada)` en ventana de **N horas** — mismo patrón que otros monitores (consulta + `Limit(1)` o hash en memoria por ciclo).

### C.3 Índices

- Si el dashboard filtra por `tipo` + `fechaHora`, documentar índice compuesto en `firestore.indexes.json` al igual que alertas de puertos.

---

## Parte D: Configuración (`Umbrales` / appsettings)

| Clave sugerida | Rol |
|----------------|-----|
| `IntervaloFirmaServiciosHoras` | Periodo entre pasadas completas (default 1). |
| `FirmaServiciosHabilitado` | bool — pilotaje / apagado rápido. |
| `FirmaServiciosSoloNoBase` | bool — si true, solo evaluar servicios que ya son “no-base” según whitelist embebida (menos CPU). |
| `ServiciosFirmaExcluidos` | Lista SCM opcional para software corporativo sin firma. |

---

## Parte E: Integración en `Program.cs`

Registrar el hosted service junto al resto:

```csharp
servicios.AddHostedService<MonitorServiciosFirmaDigitalService>(); // nombre final al implementar
```

Inyectar: `IOptions<UmbralesSettings>`, `IOptions<FirebaseSettings>`, `ILogger<>`, `IFirebaseAlertService` (o servicio de alertas dedicado coherente con el código actual).

---

## Prueba integrada

1. **Servicio legítimo Microsoft** (`services.exe`, etc.) → no alerta (firma válida en SO limpio).
2. **Copia renombrada** de un ejecutable sin firma en una ruta temporal registrada como servicio de prueba → alerta esperada (entorno de laboratorio).
3. **Sin Firebase** → solo logs; con Firebase → documento en `alertas` con tipo nuevo.
4. **Exclusión en lista** → no alerta para ese nombre SCM.

---

## Fuera de alcance (explícito)

- Firmar o cosignar binarios propios del proyecto (pipeline de release).
- Sustituir antivirus o sistema de reputación en la nube.
- Validación kernel-mode (.sys) con reglas distintas de usuario sin diseño aparte.

---

## Orden de trabajo recomendado

1. Interfaz `IValidadorFirmaEjecutable` + implementación MVP (.NET) en Windows.
2. Normalización de `PathName` compartida / reutilizada desde lógica existente de servicios.
3. `MonitorServiciosFirmaDigitalService` + umbrales + alertas Firestore.
4. Pruebas unitarias con binarios de prueba (firmado / no firmado) en carpeta de test o generados en runtime.
5. README + FUNCIONALIDADES + reglas/índices si aplica.

---

## Notas

- Pilotaje con **`FirmaServiciosHabilitado: false`** hasta validar falsos positivos en entornos con software legacy sin firma.
- Documentar en README la diferencia entre **“servicio no listado en whitelist”** (iteración 3/Analizador) y **“binario sin firma válida”** (iteración 6).
