# CyberWatch — Documentación por iteración

Este directorio incluye un archivo **`iteracion-N-pasos.md`** por cada hito mayor de desarrollo, en el mismo espíritu que el proyecto **MiniAgente-Inventario** (`MiniAgente-Inventario/docs/iteracion-*-pasos.md`): objetivos, partes (Service / Front), pasos numerados, prueba integrada y notas.

## Índice actual

| Archivo | Contenido |
|--------|-----------|
| [iteracion-1-pasos.md](iteracion-1-pasos.md) | Detección ETW, alertas ransomware, Shared, registro de instancia, kill y cuarentena (producto base consolidado). |
| [iteracion-2-pasos.md](iteracion-2-pasos.md) | Comandos remotos y OTA, UserAgent (pipe, capturas, GPS), historial de navegación. |
| [iteracion-3-pasos.md](iteracion-3-pasos.md) | SecurityEventMonitor, servicios Windows no base, dashboard e índices Firestore. |
| [iteracion-4-pasos.md](iteracion-4-pasos.md) | Monitor de puertos TCP IPv4 (`GetExtendedTcpTable`, whitelist / sospechosos, subcolección `puertos_abiertos`, alertas). |
| [iteracion-5-pasos.md](iteracion-5-pasos.md) | Entropía Shannon como refuerzo del score (opcional, `EntropiaHabilitada`); documento Firestore `config/red` con listener, unión con whitelist embebida y exclusión de procesos para alertas de puertos. |
| [iteracion-6-pasos.md](iteracion-6-pasos.md) | Validación **Authenticode** en binarios de servicios **Running** (`MonitorServiciosFirmaDigitalService`, cadena X.509); tipo `servicio_sin_firma_valida`; configurable por `Umbrales`; evolución posible WinVerifyTrust / AuthenticodeCheck. |

Las iteraciones **1 a 6** están cerradas en código (salvo mejoras puntuales).

El **orden** sigue dependencias técnicas, no necesariamente la fecha exacta de cada commit.

## Documentos relacionados (catálogo y backlog)

| Documento | Uso |
|-----------|-----|
| [FUNCIONALIDADES_CYBERWATCH.md](FUNCIONALIDADES_CYBERWATCH.md) | Catálogo **único** de comportamiento actual del código (referencia larga). |
| [pendientes.md](pendientes.md) | Backlog priorizado (lo que **falta**). |
| [Propuesta_v1.md](Propuesta_v1.md) | MVP y visión original. |
| [ArquitC2.md](ArquitC2.md), [ideas.md](ideas.md) | Arquitectura / ideas futuras. |
| [DEPLOY.md](DEPLOY.md), [NAVEGACION.md](NAVEGACION.md) | Despliegue e historial en UI. |

### Flujo al cerrar una iteración

1. Implementar y validar según `docs/iteracion-N-pasos.md`.
2. Actualizar **[README.md](../README.md)** en la raíz del repo con un resumen de las **nuevas funcionalidades** (lo que cambia para operación e integración).
3. Actualizar **[FUNCIONALIDADES_CYBERWATCH.md](FUNCIONALIDADES_CYBERWATCH.md)** con el comportamiento técnico completo alineado al código.
4. Ajustar este índice si la iteración pasa de “planificada” a “entregada”.

Para **planificar** nueva funcionalidad: crear o editar `iteracion-N-pasos.md` antes de codificar; al cerrarla, seguir los pasos anteriores.
