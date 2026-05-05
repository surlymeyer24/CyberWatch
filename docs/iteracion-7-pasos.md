# Iteración 7 — Monitor de servicios anómalos: firma, hash SHA-256 y lista blanca remota (`config/servicios`)

**Alcance:** CyberWatch.Service (+ Shared + Firebase). Evoluciona la línea de **servicios Windows** ya cubierta por whitelist estática (`ServiciosDesconocidosService`), firma local opcional (**iteración 6**) y añade una política **operativa centralizada**: exclusión por **nombre SCM** y lista blanca por **hash del binario** almacenada en Firestore, actualizada en vivo sin redesplegar agentes.

**Estado:** entregada en código (listener `config/servicios`, `MonitorServiciosAnomalos`, umbrales y documentación alineada).

---

## Contexto y decisiones de dominio

### Por qué hash + Firestore además de firma

| Capa | Rol | Límite |
|------|-----|--------|
| **Firma Authenticode** | Confianza criptográfica en el publicador e integridad del archivo. | Certificados revocados, binarios legacy sin firma, herramientas internas autofirmadas con política corporativa especial. |
| **Hash SHA-256 del PE** | Identidad **exacta** del contenido en disco; el atacante no puede igualar el hash sin igualar el archivo byte a byte. | Cada actualización del software **cambia** el hash → hay que mantener la lista en Firestore (o automatizar ingestión). |
| **Nombre SCM (`nombres_excluidos`)** | Convivencia para software que **cambia de hash con frecuencia** (CI/CD) pero nombre de servicio estable; misma filosofía que exclusiones por proceso en ransomware. | No protege contra **suplantación de nombre** si el malware usa el mismo nombre SCM que un legítimo — por eso la firma y el hash siguen siendo necesarios cuando aplique. |

**Lista blanca gestionada en Firestore** es la alternativa más **realista y segura** para entornos empresariales: un único documento remoto, listener en tiempo real (mismo patrón que `config/red`), sin tocar `appsettings.json` en cientos de endpoints.

### Relación con la iteración 6

- **`MonitorServiciosFirmaDigitalService` (iteración 6):** alerta `servicio_sin_firma_valida` cuando la cadena X.509 no es confiable, con filtros locales (`Umbrales`, whitelist embebida opcional).
- **Iteración 7:** flujo orientado a **política remota** (`config/servicios`): tras fallar la verificación de firma (según criterio acordado, p. ej. `X509Certificate2.Verify()` o lectura desde PE + cadena), se calcula **SHA-256** del ejecutable; si el hash está en **`hashes_permitidos`** o el nombre SCM en **`nombres_excluidos`**, **no** se alerta; en caso contrario, alerta **`servicio_no_firmado`**.

**Coexistencia:** si ambos monitores están habilitados, puede haber **solapamiento** de alertas para el mismo servicio. Operación recomendada: habilitar **uno** de los dos en producción o diferenciar severidad/dedup en el dashboard.

---

## Documento Firestore oficial — `config/servicios`

**Ruta:** colección **`config`**, documento **`servicios`** → `config/servicios`.

Ejemplo canónico (referencia en repo): [config-servicios.example.json](config-servicios.example.json).

```json
{
  "nombres_excluidos": ["MiAgenteInterno", "BuildRunner"],
  "hashes_permitidos": [
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
  ],
  "ultima_modificacion": "2026-05-05T18:00:00Z"
}
```

| Campo | Tipo | Uso |
|-------|------|-----|
| `nombres_excluidos` | `array<string>` | Nombres cortos **SCM** (`ServiceName`) que **omitirán** la alerta aunque firma/hash fallen las comprobaciones configuradas (software interno que rotará binarios seguido). Comparación sin distinguir mayúsculas; aceptar con o sin `.exe` si en algún flujo se confunde con nombre de proceso — el identificador principal es el nombre SCM. |
| `hashes_permitidos` | `array<string>` | SHA-256 del archivo ejecutable en **hexadecimal minúsculas** (64 caracteres). Si tras fallar la firma el hash del binario coincide, **no alertar**. |
| `ultima_modificacion` | `string` (ISO 8601 UTC) | Auditoría; opcional para logs del Service al aplicar snapshot. |

**Suscripción en tiempo real:** igual que **`ConfigRedFirestoreService`**: snapshot inicial (`GetSnapshotAsync`) + **`Listen`** sobre la `DocumentReference`; caché thread-safe en memoria; los ciclos del monitor usan siempre el último estado **sin reiniciar** el servicio Windows.

**Reglas Firestore:** lectura según modelo del proyecto (Admin SDK del agente ignora reglas de cliente); escritura solo operadores / backend autorizado (las reglas genéricas `match /config/{doc}` del repo suelen tener `write: false` para clientes).

---

## Objetivos

1. **`ConfigServiciosFirestoreService`:** modelo C# con `[FirestoreProperty]` alineado al JSON (`nombres_excluidos`, `hashes_permitidos`, `ultima_modificacion`), listener + caché (`IConfigServiciosCache` o equivalente).
2. **`MonitorServiciosAnomalos`:** `BackgroundService` que enumeración periódica de servicios **en ejecución** vía **WMI** `Win32_Service` (`State = 'Running'`), lectura de `PathName` / `Name`.
3. **Ruta binario:** reutilizar **`ServicioWindowsPaths`** (`NormalizarImagePath`, `ExtraerRutaBinarioFirma`, `NormalizarPathParaFileSystem`) y/o registro SCM ya expuesto en `ServiciosDesconocidosService` donde aplique.
4. **Firma:** validación con **`X509Certificate2`** cargado desde el PE firmado (`CreateFromSignedFile` → `X509Certificate2`) y **`Verify()`** como capa solicitada para esta iteración (documentar diferencias frente a `X509Chain.Build` / WinVerifyTrust si se comparan).
5. **Si la firma no es válida:** calcular **SHA-256** del archivo; si **no** está en `hashes_permitidos` **y** el nombre SCM **no** está en `nombres_excluidos`, emitir alerta tipo **`servicio_no_firmado`** (Firebase: subcolección `alertas` de la instancia; integración con **`FirebaseAlertService`** si se amplía la interfaz, o mismo patrón `AddAsync` que otros monitores).
6. **Dedup** y **umbrales** (`Umbrales`): intervalo de escaneo, toggle de habilitación, ventana horaria anti-spam por `(machineId, nombreServicio)` o similar.
7. **Índices Firestore** si las queries de dedup lo requieren (`tipo` + `nombreServicio` + `fechaHora` pueden reutilizar el patrón de la iteración 6).

---

## Parte A: Servicio de configuración (`ConfigServiciosFirestoreService`)

1. Propiedades en **`FirebaseSettings`** (opcional): `FirestoreConfigServiciosDocumentId` = `"servicios"` si la colección sigue siendo `config`.
2. Clase **`ConfigServiciosDocument`** en Shared con listas tipadas.
3. Caché expuesta: `HashSet<string>` de hashes normalizados a minúsculas; conjunto de nombres SCM excluidos con comparador insensible a mayúsculas.
4. Logs al aplicar snapshot: conteos, `ultima_modificacion`.

---

## Parte B: Monitor (`MonitorServiciosAnomalos`)

### B.1 Enumeración WMI

Consulta sugerida:

```text
SELECT Name, PathName FROM Win32_Service WHERE State = 'Running'
```

Usar `ManagementObjectSearcher` / `ManagementObject` con liberación adecuada de objetos.

### B.2 Lógica por servicio

1. Obtener `PathName` → expandir y extraer ruta del PE.
2. Si el archivo no existe → log (y política: omitir o alerta según producto).
3. Intentar firma: extraer certificado del PE y **`X509Certificate2.Verify()`** según especificación de esta iteración.
4. Si **Verify() == true** → servicio OK para esta pasada; siguiente.
5. Si firma no válida o ausente → `SHA256` del contenido del archivo (streaming recomendado para `.exe` grandes).
6. Consultar caché remota: si `hash ∈ hashes_permitidos` **o** `Name ∈ nombres_excluidos` → no alertar.
7. Si no → crear **`Alerta`** con `tipo = servicio_no_firmado`, `nombreServicio`, `detalle` con ruta y hash, campo opcional **`hashEjecutableSha256`** si se modela en Shared.

### B.3 Frecuencia

Intervalo largo por defecto (p. ej. **60 minutos**) configurable para no saturar WMI ni disco.

---

## Parte C: Alertas y modelo

| Campo sugerido | Valor / notas |
|----------------|---------------|
| `tipo` | `servicio_no_firmado` |
| `nombreServicio` | Nombre SCM |
| `descripcion` | Texto legible (“Servicio sin firma válida y fuera de listas blancas…”) |
| `detalle` | Ruta del binario, hash SHA-256, fragmento de política aplicada |
| `hashEjecutableSha256` | Opcional en esquema `Alerta` para filtros en dashboard |

**Dedup:** ventana configurable (horas); misma tupla `tipo` + `nombreServicio` + `fechaHora >= desde`.

---

## Parte D: Configuración (`Umbrales` / `appsettings.json`)

| Clave sugerida | Rol |
|----------------|-----|
| `MonitorServiciosAnomalosHabilitado` | `bool`, default `false` hasta pilotaje. |
| `IntervaloServiciosAnomalosMinutos` | Intervalo entre pasadas completas. |
| `DedupServiciosAnomalosHoras` | Ventana de deduplicación de alertas. |

---

## Parte E: Integración en `Program.cs`

Registrar (orden recomendado: **config listener primero**, luego monitor):

```csharp
// ConfigServiciosFirestoreService + IConfigServiciosCache
// MonitorServiciosAnomalos
```

---

## Prueba integrada

1. **Documento vacío o sin hashes:** un binario interno sin firma debe alertar si no está en listas (entorno de test controlado).
2. **Hash permitido en Firestore:** mismo binario → sin alerta tras actualizar `config/servicios` (listener refleja cambio sin reinicio del Service).
3. **Nombre en `nombres_excluidos`:** sin alerta aunque el hash cambie entre versiones.
4. **Servicio Microsoft típico:** firma `Verify()` true → sin alerta.

---

## Fuera de alcance (explícito)

- Ingestión automática de hashes desde pipeline CI (podría enlazarse en iteración futura).
- Sustituir antivirus o reputación en la nube.
- Unificar de golpe con **`MonitorServiciosFirmaDigitalService`**; la convivencia es decisión de operación.

---

## Orden de trabajo recomendado

1. Shared: `ConfigServiciosDocument`, constante de tipo de alerta, campos opcionales en `Alerta`.
2. `ConfigServiciosFirestoreService` + interfaz de caché.
3. `MonitorServiciosAnomalos` + SHA-256 + `Verify()`.
4. `FirebaseSettings` / `Umbrales` / `Program.cs`.
5. Ejemplo JSON en repo, índices Firestore si hace falta, README y FUNCIONALIDADES.

---

## Notas

- Pilotaje con **`MonitorServiciosAnomalosHabilitado: false`** hasta validar listas y falsos positivos.
- Documentar en README la diferencia entre alertas **`servicio_sin_firma_valida`** (iteración 6) y **`servicio_no_firmado`** (iteración 7): la segunda incorpora **política remota** por hash y nombre.
