# CyberWatch — Backlog de desarrollo

## 🔴 Alta prioridad

- [ ] SecurityEventMonitor — leer Event Log de Windows (IDs: 1116, 7036, 4732, 4625)
- [ ] BitLocker status en registro de instancia (WMI `Win32_EncryptableVolume`)
- [ ] Admins locales en registro de instancia (WMI `Win32_GroupUser`)
- [ ] Indicador de salud 🟢🟡🔴 por equipo en Dashboard

## 🟡 Media prioridad

- [ ] Notificaciones Windows en UserAgent cuando llega amenaza por Named Pipe (toast notifications)
- [ ] Score de riesgo numérico por equipo (0–100) calculado en el Service
- [ ] Detección de Firewall desactivado (WMI `NetFwPolicy2`)
- [ ] Limpiar documentos huérfanos en `cyberwatch_instancias` (mismo hostname, distinto machineId)

## ⚪ Baja prioridad / Futuro (v3)

- [ ] Detección C2 / dominios maliciosos (DNS queries sospechosas)
- [ ] Monitoreo de registry (claves de persistencia de malware)
- [ ] Correlación de eventos entre múltiples máquinas
- [ ] Aislamiento automático de red ante amenaza confirmada
- [ ] Tray icon en UserAgent (visible para el usuario)
