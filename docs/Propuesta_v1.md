Módulo de seguridad preventiva para endpoints Windows — Versión 1 (MVP)

---

# 🎯 Objetivo

Brindar monitoreo preventivo y detección temprana de incidentes en endpoints mediante:

- Supervisión del estado de seguridad
- Detección de eventos críticos
- Alertas centralizadas
- Base para futura automatización

> Sin competir con un EDR comercial.
> 

---

# 🧱 Arquitectura General

El servicio será un módulo dentro del agente instalado en cada PC.

```
SecurityService
 ├── SecurityStatusMonitor
 ├── SecurityEventMonitor
 └── AlertDispatcher
```

---

# 1️⃣ SecurityStatusMonitor

Se ejecuta cada X minutos y recolecta:

### 🦠 Antivirus

- ¿Está activo?
- ¿Protección en tiempo real ON?
- ¿Firmas actualizadas?

### 🔥 Firewall

- ¿Está activo?

### 🪟 Windows Update

- ¿Hay parches pendientes?
- Fecha del último update

### 🔒 BitLocker

- ¿Disco cifrado?

### 👤 Usuarios

- ¿Quiénes son admin local?

**Ejemplo de respuesta JSON:**

```json
{
  "antivirus_active": true,
  "firewall_active": true,
  "bitlocker": false,
  "admins": ["Administrador", "Juan"],
  "last_update_days": 12
}
```

---

# 2️⃣ SecurityEventMonitor

Consulta logs de Windows para detectar:

- **Event ID 1116** — Malware detectado
- Servicio Defender detenido
- Usuario agregado al grupo Administradores
- Firewall desactivado

> Solo eventos nuevos (guarda el último timestamp procesado).
> 

---

# 3️⃣ AlertDispatcher

Si detecta alguna de estas condiciones:

- Antivirus desactivado
- Malware detectado
- Usuario admin nuevo

Entonces ejecuta:

- Enviar alerta inmediata al servidor
- Marcar equipo como **"Crítico"**
- Registrar incidente

> Sin acciones destructivas automáticas en v1.
> 

---

# 📊 Dashboard esperado

El servidor mostrará el estado por equipo:

- 🟢 **Saludable**
- 🟡 **Advertencia**
- 🔴 **Crítico**

---

# ✅ Alcance técnico realista

- Solo lectura de estado
- Solo lectura de logs
- Sin driver kernel
- Sin IA
- Sin interceptar syscalls

---

# 📈 Evolución futura

### v2

- Detección de escritura masiva
- Detección de procesos sospechosos
- Score de riesgo por equipo

### v3

- Aislamiento automático de red
- Correlación de eventos
- Playbooks de respuesta