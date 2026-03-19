# Despliegue de CyberWatch

Guía para generar el paquete de instalación y desplegarlo en máquinas Windows.

---

## 1. Requisitos previos

### En el repositorio (para release por GitHub Actions)

- **Secret `FIREBASE_SERVICE_ACCOUNT`** en GitHub: contenido del JSON de la cuenta de servicio de Firebase (Firebase Console → Cuentas de servicio → Generar nueva clave privada). Sin este secret, el workflow no puede inyectar credenciales en el ZIP.

### En tu máquina (para publicar localmente)

- .NET 8 SDK  
- Archivo de credenciales Firebase: `auth/serviceAccountKey.json` (o ruta que indiques al script)

---

## 2. Opción A: Desplegar vía GitHub (recomendado para producción)

El workflow se dispara al **pushear un tag** `v*.*.*` (ej. `v1.0.0`).

### Pasos

1. **Commitear y pushear** los cambios que quieras incluir en el release.

2. **Crear el tag y subirlo** (reemplazá la versión si hace falta):

   ```powershell
   git tag v1.0.0
   git push origin v1.0.0
   ```

3. En **GitHub → Actions** se ejecuta el workflow. Al terminar:
   - En **Releases** aparece el nuevo release con el archivo **CyberWatch.Service.zip**.

4. **En la máquina destino**: descargar el ZIP del release, descomprimir, ejecutar **install.bat** como administrador (clic derecho → “Ejecutar como administrador”).

---

## 3. Opción B: Publicar localmente (para pruebas)

Podés generar el mismo paquete en tu PC sin usar GitHub.

### Script incluido

En la raíz del repo hay un script **`publish-local.ps1`** que:

- Publica `CyberWatch.Service` y `CyberWatch.UserAgent` (win-x64, self-contained).
- Junta todo en una carpeta `publish_output` (sin pisar el `appsettings.json` del Service).
- Opcionalmente copia el JSON de credenciales y parchea `CredentialPath` en `appsettings.json`.
- Copia `install.bat` al output.
- Opcionalmente crea un ZIP.

### Uso

```powershell
# Desde la raíz del repo (donde está publish-local.ps1)
cd C:\Users\Usr\Documents\CyberWatch

# Con archivo de credenciales (ruta por parámetro)
.\publish-local.ps1 -CredentialPath "C:\ruta\a\serviceAccountKey.json"

# O con archivo en auth/ dentro del repo
.\publish-local.ps1 -CredentialPath "auth\serviceAccountKey.json"

# Sin inyectar credenciales (solo build; configurás CredentialPath a mano después)
.\publish-local.ps1

# Generar también el ZIP
.\publish-local.ps1 -CredentialPath "auth\serviceAccountKey.json" -CreateZip
```

El resultado queda en **`publish_output`** (y opcionalmente **`CyberWatch.Service.zip`**). Podés copiar esa carpeta o el ZIP a la máquina destino y ejecutar **install.bat** como administrador.

---

## 4. Cuando la actualización remota no funciona

Si el comando **actualizar_agente** desde el Dashboard no aplica la nueva versión (el servicio se detiene pero al reiniciar sigue la versión antigua), podés **actualizar manualmente** con el mismo paquete que usarías para una primera instalación.

### Pasos (en la PC a actualizar)

1. **Conseguir el paquete actualizado**
   - **Opción A:** En GitHub → **Releases** → descargar el ZIP del último release (ej. `CyberWatch-v2.1.0.zip`).
   - **Opción B:** En tu máquina de desarrollo, generar el paquete local:
     ```powershell
     .\publish-local.ps1 -CredentialPath "auth\serviceAccountKey.json" -CreateZip
     ```
     Luego copiar la carpeta **`publish_output`** o el **`CyberWatch.Service.zip`** a la PC destino (USB, red, etc.).

2. **En la PC donde ya está instalado CyberWatch**
   - Copiar el contenido del ZIP (o de `publish_output`) a una carpeta temporal, ej. `C:\Temp\CyberWatchUpdate`.
   - Abrir **PowerShell o Símbolo del sistema como administrador** (clic derecho → “Ejecutar como administrador”).
   - Ir a esa carpeta y ejecutar:
     ```powershell
     cd "C:\Temp\CyberWatchUpdate"
     .\install.bat
     ```

3. **Qué hace `install.bat`**
   - Copia todos los archivos (incluidos los .exe y `appsettings.json`) a **`C:\Program Files\CyberWatch`**, sobrescribiendo los existentes.
   - Si el servicio ya existe: lo detiene, actualiza la ruta del binario y lo inicia de nuevo.
   - No borra `serviceAccountKey.json` si ya estaba; si el ZIP incluye credenciales, las sobrescribe.

Tras ejecutar `install.bat`, el servicio y el UserAgent quedan con la nueva versión. No hace falta desinstalar nada: es una actualización en el mismo directorio.

---

## 5. Instalación en la máquina destino (primera vez)

1. Copiar la carpeta descomprimida (o el contenido del ZIP) a la máquina (ej. Escritorio o `C:\Temp\CyberWatch`).
2. Abrir una ventana **como administrador** (clic derecho en Símbolo del sistema o PowerShell → “Ejecutar como administrador”).
3. Ir a la carpeta:
   ```powershell
   cd "C:\Temp\CyberWatch"   # o la ruta donde descomprimiste
   ```
4. Ejecutar:
   ```powershell
   .\install.bat
   ```
5. Si aparece el error de privilegios, cerrar y ejecutar **install.bat** con clic derecho → “Ejecutar como administrador”.

Tras la instalación:

- El **servicio CyberWatch** queda instalado e iniciado en Session 0.
- Al **iniciar sesión** cada usuario, el Programador de tareas ejecuta **CyberWatch.UserAgent.exe** (registrado por el servicio).

---

## 6. Resumen del contenido del paquete

| Archivo / carpeta        | Origen        | Uso |
|--------------------------|---------------|-----|
| CyberWatch.Service.exe   | Service       | Servicio Windows (Session 0). |
| CyberWatch.UserAgent.exe | UserAgent     | Agente en sesión de usuario (arranca por tarea al logon). |
| appsettings.json         | Service       | Config; en release tiene `CredentialPath`: `serviceAccountKey.json`. |
| serviceAccountKey.json   | Secret / local| Credenciales Firebase; debe estar junto a los .exe. |
| install.bat              | Repo          | Instalador (requiere admin). |

---

## 7. Una PC no aparece en el dashboard

El dashboard muestra todas las instancias de la colección **cyberwatch_instancias** en Firestore. Si instalaste en otra PC y no la ves:

### Comprobar en la PC que no aparece

1. **Credenciales**  
   En `C:\Program Files\CyberWatch\` debe existir **serviceAccountKey.json**. Si no está, el servicio no se conecta a Firestore y no se registra.  
   - Si usaste un paquete sin credenciales, copiá el mismo `serviceAccountKey.json` que usás en esta PC a esa carpeta.

2. **Servicio en ejecución**  
   Abrí **services.msc** y verificá que el servicio **CyberWatch** esté **En ejecución**. Si no arrancó, revisá el Visor de eventos (Windows → Registro de aplicaciones) o el log del servicio.

3. **Log del servicio**  
   En la otra PC abrí:
   ```text
   C:\Program Files\CyberWatch\cyberwatch_service.log
   ```
   - Si ves **"Firebase no configurado; esta instancia no se registrará en Firestore"** → falta `serviceAccountKey.json` o `CredentialPath` en `appsettings.json`.  
   - Si ves **"Registro de instancia cada 5 min (colección: cyberwatch_instancias, id: ...)"** → el servicio sí está registrando; esperá 1–2 minutos y actualizá el dashboard.  
   - Si ves **"No se pudo conectar a Firestore"** o errores de red → firewall, proxy o sin internet.

4. **Esperar el primer registro**  
   El servicio registra cada **5 minutos** (configurable en `IntervaloRegistroInstanciaMinutos`). Después de instalar, puede tardar hasta 5 minutos en aparecer la primera vez.

5. **Mismo proyecto Firebase**  
   Dashboard y servicio deben usar el **mismo proyecto** (mismo `ProjectId` en Firebase) y la misma colección `cyberwatch_instancias`. Si el Dashboard apunta a otro proyecto, no verás esa PC.

### Si los datos no se suben a Firestore

En la PC donde está instalado el servicio, revisá:

1. **`C:\Program Files\CyberWatch\serviceAccountKey.json`**  
   Tiene que existir en esa carpeta. Si no está, el servicio no puede conectarse a Firestore.

2. **`C:\Program Files\CyberWatch\appsettings.json`**  
   En la sección `Firebase`:
   - **CredentialPath** debe ser `"serviceAccountKey.json"` (ruta relativa a la carpeta del .exe), **o** estar vacío `""`; en ese caso el servicio busca `serviceAccountKey.json` en la misma carpeta.
   - Si **CredentialJson** tiene una ruta de tu PC de desarrollo (ej. `C:/Users/.../auth/serviceAccountKey.json`), vacialo en la PC desplegada: `"CredentialJson": ""`. Ese campo es para pegar el *contenido* del JSON, no una ruta; si dejás una ruta, las credenciales fallan.

3. **Log**  
   Abrí `C:\Program Files\CyberWatch\cyberwatch_service.log`:
   - **"Registro de instancia cada 5 min (colección: cyberwatch_instancias, id: ...)"** → está registrando en Firestore; puede tardar hasta 5 minutos en verse en el dashboard.
   - **"Firebase no configurado"** → no encuentra credenciales; verificá los puntos 1 y 2.
   - **"No se pudo conectar a Firestore"** o excepciones al iniciar → credenciales inválidas, red o firewall.

Tras cambiar `appsettings.json` o agregar `serviceAccountKey.json`, reiniciá el servicio CyberWatch en **services.msc**.
