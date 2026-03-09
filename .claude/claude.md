# Lecciones aprendidas — CyberWatch

## Firebase Storage: no usar PredefinedObjectAcl.PublicRead

**Error cometido:** Al subir archivos a Firebase Storage con `Google.Cloud.Storage.V1`,
se usó `PredefinedObjectAcl.PublicRead` en las opciones de upload:

```csharp
await storageClient.UploadObjectAsync(bucket, objectName, "image/png", fileStream,
    new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.PublicRead }); // ❌
```

**Por qué falla:** Los buckets de Firebase Storage tienen "Uniform bucket-level access"
activado por defecto, que deshabilita los ACLs a nivel de objeto. Esto lanza una
`GoogleApiException` que es capturada silenciosamente, impidiendo la ejecución del
código posterior (actualización de Firestore).

**Fix correcto:** Subir sin ACL y generar una URL firmada (signed URL):

```csharp
await storageClient.UploadObjectAsync(bucket, objectName, "image/png", fileStream); // ✅

var urlSigner = UrlSigner.FromCredential(credential);
var url = await urlSigner.SignAsync(bucket, objectName, TimeSpan.FromDays(365)); // ✅
```

**También:** Usar `SetAsync(..., SetOptions.MergeAll)` en lugar de `UpdateAsync`
para Firestore, ya que `UpdateAsync` lanza si el documento no existe.

---

## Machine ID: usar UUID de hardware (WMI), no Guid.NewGuid()

**Error cometido:** Se recomendó usar `Guid.NewGuid()` como identificador de máquina,
guardado en `cyberwatch_machine_id.txt`. Esto genera duplicados en Firestore si el
archivo se borra, el servicio se reinstala, o cambia el directorio de instalación.
Cada reinstalación crea un documento huérfano en `cyberwatch_instancias`.

**Fix correcto:** Derivar el ID del UUID de hardware de la máquina via WMI, que es
estable entre reinstalaciones:

```csharp
// Requiere paquete: System.Management
using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
foreach (ManagementObject obj in searcher.Get())
{
    var uuid = obj["UUID"]?.ToString()?.Trim().ToLower();
    if (!string.IsNullOrEmpty(uuid) && uuid != "ffffffff-ffff-ffff-ffff-ffffffffffff")
        return uuid;
}
// Fallback: Guid.NewGuid() guardado en archivo
```

**Regla:** Para cualquier identificador que deba ser estable por máquina física,
usar siempre UUID de hardware. Nunca confiar en archivos locales como fuente
de identidad única.
