# 🟢 Servicio Preventivo

## 🎯 Requerimientos

- Comportamientos sospechosos
- Ransomware / Movimiento lateral
- Escalada de privilegios
- Dominios maliciosos / Phishing
- C2 (Command & Control)
- Sitios peligrosos
- GPS de máquinas → realtime

## 🧱 Stack técnico

- C# + POO + FileSystemWatcher
- Windows Service

**Clases principales:** `FileActivityMonitor` · `ProcessActivityTracker` · `ThreatEvaluator`

## 💡 Ideas

### 🛡️ Detección basada en comportamiento

- Escritura/modificación masiva de archivos en corto tiempo
- Renombrado masivo de archivos (cambio de extensión)
- Procesos desconocidos con I/O de disco alto
- Escritura simultánea en múltiples directorios

### ⚙️ Consideraciones de implementación

- Monitor de tasa de escritura con umbral razonable
- Exclusiones inteligentes
- Reporte central
- No acción automática fuerte hasta validar

# 🟡 Servicio Control de Usuario

- Captura periódica de actividad del usuario → cloud function
- GPS de máquinas en tiempo real

---

# ⚙️ Config — Auto-actualización

Para que la **auto-actualización** funcione, en Firestore → `config/actualizaciones` → objeto `cyberwatch` debe tener:

- **version:** ej. `"1.0.0"`
- **url:** URL del **archivo ZIP** del release (no del `.exe`)

Si en GitHub solo tenés subido el `.exe`, creá un ZIP con la carpeta de publicación (`dotnet publish`) y subilo como asset del mismo release. Esa URL va en `cyberwatch.url`.