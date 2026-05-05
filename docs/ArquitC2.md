# Arquitectura de 2 Componentes

## Arquitectura de dos componentes propuesta

### Proyecto existente: `CyberWatch.Service` (Session 0)

Se mantiene como está. Agrego un **Named Pipe Server** para comunicarse con el agente.

### Proyecto nuevo: `CyberWatch.UserAgent` (Sesión de usuario)

App WinForms liviana (para poder tener tray icon) que corre en la sesión del usuario.

`┌─────────────────────────────────────┐     Named Pipe      ┌──────────────────────────────┐
│   CyberWatch.Service (Session 0)    │ ◄──────────────────► │  CyberWatch.UserAgent        │
│                                     │                       │  (Sesión usuario)            │
│  - Detección de ransomware          │  Eventos:             │                              │
│  - Alertas Firestore                │  → "amenaza_detectada"│  - Windows Location API      │
│  - IP Geolocalización (ya hecho)    │  ← "ubicacion:{...}" │  - Captura de pantalla       │
│  - Registro de instancia            │  ← "screenshot_ready"│  - Tray icon + notificaciones│
└─────────────────────────────────────┘                       └──────────────────────────────┘
                                                                          │
                                                                          ▼ escribe a
                                                                    Firestore
                                                              (campos: lat_gps, lon_gps,
                                                               screenshot_url, etc.)`

### Comunicación: Named Pipe

Mensajes JSON simples:

`// Service → UserAgent (amenaza detectada)
{"tipo": "amenaza", "proceso": "crypto.exe", "timestamp": "2026-03-06T..."}

// UserAgent → Service (responde con ubicación GPS)
{"tipo": "ubicacion", "lat": -34.6, "lon": -58.4, "precision": 15.0}`

### Auto-start del UserAgent

Registro en **Task Scheduler** (no en `HKCU\Run`) con trigger "al iniciar sesión" — más robusto y no requiere que el usuario lo vea.

---

## Plan de implementación

1. **Named Pipe Server** en `CyberWatch.Service`
2. **Proyecto `CyberWatch.UserAgent`** con WinForms + tray icon
3. **Windows Location API** en el UserAgent
4. **Captura de pantalla** en el UserAgent (activada por evento del Service)
5. **Auto-start** vía Task Scheduler (instalado por el Service)

---

¿Te parece bien este diseño? ¿Arrancamos por el Named Pipe en el Service o preferís empezar por el proyecto del UserAgent?

El userAgent que mostraria en el winForms?

Nada, o casi nada. El UserAgent sería una app WinForms **sin ventana visible** — corre como **tray icon** (ícono en la barra del sistema, al lado del reloj), igual que un antivirus.

`[^][^][^]  🛡️  ←── CyberWatch en la bandeja`

Al hacer click derecho en el ícono:

`┌─────────────────────────┐
│ 🛡️ CyberWatch Agent     │
│─────────────────────────│
│  Estado: Protegido ✓    │
│  Última amenaza: ninguna│
│─────────────────────────│
│  Salir                  │
└─────────────────────────┘`

Y cuando el Service detecta una amenaza, el UserAgent dispara una **notificación de Windows**:

`┌─────────────────────────────────┐
│ 🛡️ CyberWatch          [x]     │
│ ⚠️ Amenaza detectada            │
│ Se terminó el proceso crypto.exe│
└─────────────────────────────────┘`

---

Si no querés ni el tray icon, podés usar directamente una **Console app** con `AttachConsole` que corra invisible en background. Sería aún más simple.

¿Preferís el tray icon (más profesional, el usuario sabe que está corriendo) o invisible total?

Sí, el despliegue puede ser totalmente transparente.

## Cómo funciona

El **Windows Service** (que ya tiene privilegios de SYSTEM) al arrancar por primera vez:

1. Detecta que `CyberWatch.UserAgent.exe` está en la misma carpeta
2. Crea automáticamente una **tarea en Task Scheduler** con trigger "al iniciar sesión de cualquier usuario"
3. Listo — el UserAgent arranca invisible con cada sesión de usuario

`Instalador
    └── copia CyberWatch.Service.exe  ─► se registra como Windows Service
    └── copia CyberWatch.UserAgent.exe ─► el Service crea la tarea automáticamente`

## Para que sea invisible total

El UserAgent sería un proyecto con `<OutputType>WinExe</OutputType>` (no Console), así Windows no abre ninguna ventana de consola. Corre completamente en background.

## Flujo de despliegue final

`1. Copiar archivos a C:\Program Files\CyberWatch\
2. sc create CyberWatch ...   (o un instalador .msi)
3. Reiniciar / iniciar sesión
   └── Service arranca → crea tarea de UserAgent
   └── UserAgent arranca invisible en sesión de usuario`

---

¿Arrancamos a implementarlo? El orden lógico sería:

1. Crear proyecto `CyberWatch.UserAgent` (invisible, con Named Pipe client)
2. Agregar Named Pipe Server al `CyberWatch.Service`
3. Lógica de Location API + screenshots en el UserAgent
4. Auto-registro en Task Scheduler desde el Service