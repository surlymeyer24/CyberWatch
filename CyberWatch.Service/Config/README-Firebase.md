# Configuración Firebase en CyberWatch

## Equivalente Node.js → C# (.NET)

En Node.js se inicializa Firebase Admin así:

```javascript
var admin = require("firebase-admin");
var serviceAccount = require("path/to/serviceAccountKey.json");

admin.initializeApp({
  credential: admin.credential.cert(serviceAccount)
});
```

En este proyecto (C#) se hace lo mismo con el SDK de Firebase Admin para .NET:

```csharp
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

// Cargar el JSON de la cuenta de servicio (equivalente a require("..."))
var serviceAccount = GoogleCredential.FromFile("ruta/al/serviceAccountKey.json");

// Inicializar la app (equivalente a admin.initializeApp)
FirebaseApp.Create(new AppOptions
{
    Credential = serviceAccount,
    ProjectId = "devbac-42d14"
});
```

La ruta del JSON se configura en `appsettings.json` → `Firebase:CredentialPath`. El servicio hace esta inicialización al arrancar si `CredentialPath` está definido y el archivo existe.

---

## Datos ya configurados

En `appsettings.json` están los datos del proyecto Firebase (SDK web):

- **ProjectId:** devbac-42d14  
- **ApiKey, AuthDomain, StorageBucket, AppId, MeasurementId**  
- **CredentialPath:** vacío por defecto  

## Enviar alertas a Firestore

Para que el servicio envíe las alertas a Firestore:

1. En [Firebase Console](https://console.firebase.google.com) → tu proyecto **devbac-42d14**.
2. **Configuración del proyecto** (engranaje) → **Cuentas de servicio**.
3. **Generar nueva clave privada** y guarda el JSON en una ruta segura (ej: `C:\Config\devbac-firebase-key.json`).
4. En `appsettings.json` asigna esa ruta:
   ```json
   "Firebase": {
     ...
     "CredentialPath": "C:\\Config\\devbac-firebase-key.json",
     "FirestoreCollectionAlertas": "alertas"
   }
   ```
5. En Firestore crea la colección `alertas` (o el nombre que pongas en `FirestoreCollectionAlertas`). Los documentos se crean solos al enviar alertas.

Si `CredentialPath` está vacío o el archivo no existe, el servicio sigue funcionando y solo escribe en el log local (`cyberwatch.log`); no envía nada a Firebase.

## Actualizar el servicio desde un release de GitHub (vía Firebase)

El servicio puede comprobar en Firestore si hay una nueva versión publicada en GitHub y actualizarse solo.

### Cómo actualizar el proceso de forma remota (resumen)

1. **Publicar el release** (en tu máquina, desde el repo):
   ```bash
   cd C:\Users\Usr\Documents\CyberWatch
   dotnet publish CyberWatch.Service/CyberWatch.Service.csproj -c Release -o publish/CyberWatch.Service
   ```
   Luego crea un ZIP con todo el contenido de la carpeta `publish/CyberWatch.Service` (incluye el .exe, .dlls y appsettings.json). El nombre del archivo puede ser `CyberWatch.Service.zip`.

2. **Subir el ZIP a GitHub:** en el repo → **Releases** → **Create a new release** (o editar uno existente) → etiqueta ej. `v1.0.1` → arrastra el ZIP como asset → publicar. Copia la URL del archivo (clic derecho en el asset → "Copy link address"), p. ej.  
   `https://github.com/surlymeyer24/CyberWatch/releases/download/v1.0.1/CyberWatch.Service.zip`

3. **Actualizar Firestore:** Firebase Console → Firestore → colección **config** → documento **ciberseguridad**. Edita (o crea) el documento con:
   - **version:** `1.0.1` (la nueva versión que quieres desplegar)
   - **url:** la URL del ZIP del paso 2

4. **En las PCs:** no hace falta ejecutar nada. El servicio ya instalado consulta Firestore cada X minutos (p. ej. 60). Si la versión en Firestore es **mayor** que la que tiene instalada, descarga el ZIP, se detiene, aplica los archivos y se reinicia. Todo automático.

---

### 1. Estructura en Firestore (documento ciberseguridad)

CyberWatch usa un documento propio en la colección **config** para no mezclar con el otro proceso (**agente**).

- **Colección:** `config`
- **Documento:** `ciberseguridad` (solo para CyberWatch; el otro proceso sigue usando `agente`).
- **Campos del documento:** `version` (string) y `url` (string, URL del .zip del release).

Ejemplo del documento **config/ciberseguridad**:

```json
{
  "version": "1.0.1",
  "url": "https://github.com/surlymeyer24/CyberWatch/releases/download/v1.0.1/CyberWatch.Service.zip"
}
```

**Importante:** La URL debe apuntar a un **archivo ZIP** con la carpeta de publicación completa (exe, dlls, appsettings.json). El actualizador descarga el ZIP, lo extrae y reemplaza todos los archivos.

En `appsettings.json` del servicio:
- `Firebase:FirestoreDocumentoActualizacion`: `config/ciberseguridad`
- `FirestoreCampoActualizacion`: vacío (se usa el documento completo).

### 2. Publicar un release en GitHub

Repositorio: **https://github.com/surlymeyer24/CyberWatch/releases**. Asset actual v1.0.0 (manual): [CyberWatch.Service.exe](https://github.com/surlymeyer24/CyberWatch/releases/download/v1.0.0/CyberWatch.Service.exe). Para auto-actualización necesitas subir además un **ZIP** (ver abajo).

1. En el repo **CyberWatch** (o el que uses), ve a **Releases** → **Create a new release**.
2. Crea la etiqueta (ej: `v1.0.1`) y el título.
3. Sube un **asset** en ZIP con el contenido de la carpeta de publicación del servicio (todo lo que hay en `bin\Release\net8.0\publish\` o similar: el .exe, .dll, appsettings.json, etc.).
4. Publica el release.
5. Copia la URL del asset (clic derecho en el archivo → “Copy link address”). Esa URL del ZIP es la que pones en Firestore en `cyberwatch.url`. Ejemplo: `https://github.com/surlymeyer24/CyberWatch/releases/download/v1.0.0/CyberWatch.Service.zip`.

### 3. Configuración en el servicio

En `appsettings.json`:

```json
"CyberWatch": {
  "Version": "1.0.0",
  "ServiceName": "CyberWatchService",
  "IntervaloActualizacionMinutos": 60
}
```

- **Version:** versión actual instalada. Si en Firestore hay una versión **mayor**, se descargará e instalará la actualización.
- **ServiceName:** nombre del servicio Windows (el que usaste al instalar con `sc create` o similar).
- **IntervaloActualizacionMinutos:** cada cuántos minutos se consulta Firestore. `0` = desactivado.

### 4. Flujo automático

1. El servicio se conecta a Firebase con `CredentialPath` y consulta Firestore cada `IntervaloActualizacionMinutos`.
2. Lee el documento `config/ciberseguridad` y toma `version` y `url` del documento.
3. Compara `version` con `CyberWatch:Version` (en appsettings de la PC). Si en Firestore es mayor, descarga el ZIP desde `url`, lo extrae, genera un script que detiene el servicio, copia los archivos y reinicia el servicio.
4. El proceso se cierra y el script aplica la actualización.

Requisitos: `CredentialPath` configurado y permisos para detener/iniciar el servicio (LocalSystem suele tenerlos).

### 5. Logs: ver si la actualización se hizo, falló, etc.

Los mensajes del servicio (incluido el actualizador) se escriben en:

| Dónde | Cuándo |
|-------|--------|
| **Archivo `cyberwatch_service.log`** | Siempre. Está en la **misma carpeta** donde está el .exe del servicio (p. ej. la carpeta de instalación o `publish/CyberWatch.Service`). Abre ese archivo para ver el historial. |
| **Consola** | Solo si ejecutas con `dotnet run --project CyberWatch.Service`. |
| **Visor de eventos de Windows** | Si el servicio está instalado como servicio Windows: **Visor de eventos** → **Registros de Windows** → **Aplicación**; busca el origen con el nombre del servicio. |

**Mensajes típicos del actualizador en el log:**

- `Comprobación de actualizaciones cada 60 min (documento: config/ciberseguridad).` — arranque del comprobador.
- `Nueva versión disponible: 1.0.1 (actual: 1.0.0). Descargando...` — encontró una versión mayor en Firestore y va a descargar.
- `Actualización descargada. Reiniciando servicio para aplicar...` — descarga y extracción OK; el servicio se va a reiniciar para aplicar.
- `Error al descargar la actualización desde {url}` — fallo de red o URL incorrecta.
- `Error al extraer el ZIP.` — el archivo descargado no es un ZIP válido.
- `Faltan 'version' y 'url' en el documento de Firestore.` — el documento `config/ciberseguridad` está mal formado.
- `Ya está en la última versión (1.0.0).` — solo aparece con nivel Debug; la versión en Firestore no es mayor.

Tras un reinicio por actualización, al abrir de nuevo `cyberwatch_service.log` verás primero las líneas del cierre anterior (descarga y reinicio) y después las del nuevo arranque (Firebase, registro de instancia, etc.). La versión desplegada también se ve en Firestore en `cyberwatch_instancias` (campo `version` por máquina).

---

## Saber en qué máquinas está CyberWatch (registro de instancias)

Cada instancia de CyberWatch se registra en Firestore para que puedas ver **dónde está instalado** (igual que el otro servicio con **computadoras**).

- **Colección:** `cyberwatch_instancias` (configurable con `Firebase:FirestoreColeccionInstancias`).
- **Documento por máquina:** el ID es un GUID estable (generado la primera vez y guardado en `cyberwatch_machine_id.txt` en la carpeta del servicio).
- **Campos que escribe el servicio:** `hostname`, `version`, `ultima_conexion`, `servicio`, y opcionalmente `ip_local`.

Cada **IntervaloRegistroInstanciaMinutos** (por defecto 5) el servicio actualiza su documento. Así en la consola de Firestore ves qué máquinas tienen CyberWatch y cuándo se conectaron por última vez. Para desactivar el registro, pon `IntervaloRegistroInstanciaMinutos: 0`.

---

## Seguridad

- No subas el JSON de la cuenta de servicio al repositorio.
- En producción puedes usar **User Secrets** o variables de entorno para `CredentialPath` y `ApiKey`.

---

## Cómo probar si Firebase funciona

### 1. Probar solo la conexión (recomendado primero)

Usa el proyecto **CyberWatch.DumpFirestore**, que solo lee Firestore y vuelca el contenido a archivos:

1. Descarga el JSON de la cuenta de servicio desde Firebase Console (Configuración → Cuentas de servicio → Generar nueva clave privada).
2. Pon el JSON en una ruta segura (ej: `C:\Config\devbac-firebase-key.json`) o en `auth/serviceAccountKey.json` en la raíz del repo.
3. Si no usas `auth/serviceAccountKey.json`, configura en `CyberWatch.DumpFirestore/appsettings.json` (o en `CyberWatch.Service/appsettings.json`):
   ```json
   "Firebase": {
     "CredentialPath": "C:\\Config\\devbac-firebase-key.json",
     "ProjectId": "devbac-42d14"
   }
   ```
4. Desde la raíz del repositorio ejecuta:
   ```bash
   dotnet run --project CyberWatch.DumpFirestore
   ```
5. Si la conexión es correcta, verás mensajes leyendo colecciones y se generarán `firebase_dump.json` y `firebase_dump.md` en la carpeta del proyecto. Si falla, verás el error en consola (credenciales, proyecto, red, etc.).

### 2. Probar con el servicio (alertas + registro de instancias)

1. Configura `Firebase:CredentialPath` en `CyberWatch.Service/appsettings.json` con la ruta al JSON de la cuenta de servicio.
2. Ejecuta el servicio (como consola para ver logs):
   ```bash
   dotnet run --project CyberWatch.Service
   ```
3. En los logs busca:
   - **Funciona:** `Firebase Admin inicializado. Proyecto: devbac-42d14`
   - **No configurado:** `Firebase no configurado (falta CredentialPath)...`
   - **Error:** `No se pudo inicializar Firebase...` (revisa ruta del JSON, permisos y formato del archivo).
4. Tras unos minutos, abre [Firebase Console](https://console.firebase.google.com) → tu proyecto → **Firestore**. Deberías ver:
   - **Colección `cyberwatch_instancias`:** un documento por cada máquina donde corre el servicio (se actualiza cada 5 minutos por defecto). Si aparece tu máquina con `hostname`, `version`, `ultima_conexion`, Firebase está funcionando.
   - **Colección `alertas`:** solo tendrá documentos cuando el servicio detecte una amenaza y envíe una alerta.

### 3. Probar el Dashboard (opcional)

Si tienes el Dashboard configurado con el mismo `CredentialPath` y `ProjectId`, ejecuta:

```bash
dotnet run --project CyberWatch.Dashboard
```

Abre la URL que indique (ej: `http://localhost:5000`). Si carga datos de Firestore (alertas, instancias, config), la conexión Firebase funciona.

### Resumen rápido

| Qué quieres comprobar | Cómo |
|----------------------|------|
| ¿La cuenta de servicio y Firestore responden? | `dotnet run --project CyberWatch.DumpFirestore` y revisar que se generen los dumps. |
| ¿El servicio se conecta al arrancar? | Log: `Firebase Admin inicializado. Proyecto: ...` |
| ¿Se registra esta máquina? | Firestore → colección `cyberwatch_instancias` → documento con tu `hostname`. |
| ¿Se envían alertas? | Firestore → colección `alertas` (solo hay documentos cuando hay detecciones). |
