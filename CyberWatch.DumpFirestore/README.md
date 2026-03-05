# CyberWatch.DumpFirestore

Volcado del estado de Firestore a archivos para poder revisar la estructura y los datos (o compartirlos con el asistente).

## Requisitos

- **Cuenta de servicio:** archivo JSON de la cuenta de servicio de Firebase (mismo que usa el servicio CyberWatch).
  - Por defecto busca `auth/serviceAccountKey.json` respecto al directorio desde el que ejecutas.
  - O configura `Firebase:CredentialPath` en `appsettings.json` con la ruta correcta.

## Uso

Desde la **raíz del repositorio** (para que los archivos generados queden ahí):

```bash
dotnet run --project CyberWatch.DumpFirestore
```

O desde cualquier carpeta, si en `appsettings.json` configuraste `Salida:Directorio` con la ruta donde quieres los archivos.

## Archivos generados

- **firebase_dump.json** – Volcado completo de las colecciones configuradas (JSON).
- **firebase_dump.md** – Resumen legible por colección y documento (Markdown).

Puedes abrir `firebase_dump.md` o `firebase_dump.json` y compartir su contenido para que revisen la base de datos.

## Configuración (appsettings.json)

| Clave | Descripción |
|-------|-------------|
| `Firebase:ProjectId` | ID del proyecto Firebase (ej: devbac-42d14). |
| `Firebase:CredentialPath` | Ruta al JSON de la cuenta de servicio (ej: auth/serviceAccountKey.json). |
| `Firebase:Colecciones` | Lista de colecciones a exportar (por defecto: alertas, config, services). |
| `Salida:Directorio` | Carpeta donde escribir los archivos (vacío = directorio actual). |
| `Salida:NombreJson` | Nombre del archivo JSON (por defecto: firebase_dump.json). |
| `Salida:NombreMd` | Nombre del archivo Markdown (por defecto: firebase_dump.md). |
