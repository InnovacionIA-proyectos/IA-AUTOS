# Documentación de IA Autos

Índice de lo que hay que leer, según lo que se busque. El contrato de la API está en [backend/CONTRATO.md](../backend/CONTRATO.md) y, versionado, en [backend/docs/openapi.json](../backend/docs/openapi.json).

## Especificación de negocio

La lógica del negocio (qué es Traza Autos 360, el flujo de inspección y clasificación de averías, roles y accesos, requerimientos y diseño) vive en la carpeta de documentos de IA Autos en Drive, **«08. Trazautos360»**:

> Enlace a la carpeta: _(pegar aquí la URL de la carpeta de Drive de IA Autos)_

| Documento | De qué trata |
|---|---|
| `TRAZA Autos 360 Versión 5.pdf` y `Entendimiento IA EN AUTOS` | el entendimiento y la propuesta del negocio |
| `PRD-Autos sesion 2.md` | requerimientos del producto |
| `Architecture-Autos sesion 2.md` y `Design_system- sesion 2.md` | arquitectura y sistema de diseño |
| `Inspeccion_Prembarque_Flujo_y_Clasificacion_de_Averias v2.pdf` y `flujo_inspeccion_prembarque.png` | el flujo de inspección de preembarque y la clasificación de averías (niveles V) |
| `Validacion_roles_accesos_funciones conductor_proyecto IA Autos.docx` | roles, accesos y funciones |
| `Evaluacion_inventario_visual_vin.xlsx`, `Simulador_botero_soto.html` y `Resumen del proyecto.jpeg` | evaluación del inventario visual, simulador y resumen |

## Historias de usuario

Las historias de usuario (HU), sus tareas y sus escenarios están en GitHub: cada HU es un **milestone** del repositorio y cada tarea, una Issue de ese milestone, con su texto de la fuente, criterios de aceptación y escenarios. La matriz que cruza decisiones, HU, pruebas y estado
está en [qa/matriz-trazabilidad.md](qa/matriz-trazabilidad.md).

- **Sprint 5** — S5-HU-01 a S5-HU-31 ([todos los milestones](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestones?state=closed)): migración del backend a .NET 10 y SQL Server 2019 y el nuevo modelo (catálogos maestros, el viaje como eje, identidad por cédula y PIN, inspección de salida y de llegada, transferencia de custodia).
  - [S5-HU-20 · Sesión del portal e integración del módulo IA Autos](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/20) · [S5-HU-21 · Catálogos maestros](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/21) · [S5-HU-22 · El viaje como eje](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/22)
  - [S5-HU-23 · Identidad: cédula y PIN](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/23) · [S5-HU-24 · Inspección de salida y de llegada](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/24) · [S5-HU-25 · Transferencia de custodia](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/25) · [S5-HU-31 · Trazabilidad](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/26)
- **Sprint 6** — S6-HU-01 a S6-HU-09 ([todos los milestones](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestones)):

| HU | Qué entrega |
|---|---|
| [S6-HU-01](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/27) | hallazgo por cuadrante (lado + número), nivel V, preexistente o nuevo y seguimiento |
| [S6-HU-02](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/28) | el conductor en ruta, novedades del recorrido y alertas preventivas |
| [S6-HU-03](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/29) | evidencia inmutable y auditoría consultable |
| [S6-HU-04](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/30) | KPIs del piloto, reportes y exportaciones |
| [S6-HU-05](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/31) | listados paginados, filtros, rendimiento y visibilidad por cliente |
| [S6-HU-06](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/32) | contrato OpenAPI v2 y clientes alineados |
| [S6-HU-07](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/33) | release de la app, pruebas de campo y piloto |
| [S6-HU-08](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/34) | retiro del front Angular 22 y documentación del alcance real |
| [S6-HU-09](https://github.com/InnovacionIA-proyectos/IA-AUTOS/milestone/35) | complementos del sprint 5 |

## Dónde vive cada pieza

| Pieza | Dónde |
|---|---|
| **Web** | un **módulo del portal de administración**: repositorio [`administracion`](https://github.com/InnovacionIA-proyectos/administracion), carpeta [`src/app/ia-autos`](https://github.com/InnovacionIA-proyectos/administracion/tree/QA/src/app/ia-autos). El front Angular 22 de este repositorio (`frontend/`) queda **retirado**: la web ya no es ese front (ver [planes/PLAN-MIGRACION-ANGULAR.md](planes/PLAN-MIGRACION-ANGULAR.md)) |
| API | [backend](../backend) (.NET 10 y SQL Server 2019) |
| App de campo | [mobile](../mobile) (Flutter, con cola de envío sin conexión) |
| ETL de Sofasa | fuera de esta solución; envía las órdenes a `POST /api/etl/sofasa/ordenes` |

## Planes y arquitectura

- [Plan de migración del backend](planes/PLAN-MIGRACION-BACKEND.md), [de la web](planes/PLAN-MIGRACION-ANGULAR.md) y [de la app](planes/PLAN-MIGRACION-FLUTTER.md); diagrama en [arquitectura/Arquitectura.png](arquitectura/Arquitectura.png).
- [Guía de migración v1 → v2 del contrato](../backend/docs/migracion-v1-v2.md) (lista cerrada de cambios de ruptura).

## Documentación del backend

Cada tema tiene su documento en [backend/docs](../backend/docs); el contrato (rutas, cuerpos, respuestas y errores) está en [backend/CONTRATO.md](../backend/CONTRATO.md).

| Tema | Documento |
|---|---|
| Hallazgo por región (zonas, paneles y regiones del daño) | [zonas-dano.md](../backend/docs/zonas-dano.md) |
| Seguimiento del hallazgo y unidad que no sale | [seguimiento-hallazgos.md](../backend/docs/seguimiento-hallazgos.md) |
| Conductor en ruta | [conductor-en-ruta.md](../backend/docs/conductor-en-ruta.md) |
| Notificaciones y dispositivos | [notificaciones.md](../backend/docs/notificaciones.md) |
| Alertas preventivas y avisos operativos | [alertas.md](../backend/docs/alertas.md) |
| Auditoría | [auditoria.md](../backend/docs/auditoria.md) |
| Notas aclaratorias | [notas-aclaratorias.md](../backend/docs/notas-aclaratorias.md) |
| Parámetros | [parametros.md](../backend/docs/parametros.md) |
| Reportes e indicadores | [indicadores.md](../backend/docs/indicadores.md) y [rendimiento.md](../backend/docs/rendimiento.md) |
| Exportaciones a Excel y PDF | [exportaciones-y-pdf.md](../backend/docs/exportaciones-y-pdf.md) |
| Visibilidad por cliente | [visibilidad-por-cliente.md](../backend/docs/visibilidad-por-cliente.md) |
| Fotos con URL firmada | [fotos-firmadas.md](../backend/docs/fotos-firmadas.md) |
| Inspecciones, transferencias, viajes e identidad | [inspecciones.md](../backend/docs/inspecciones.md), [transferencias.md](../backend/docs/transferencias.md), [viajes.md](../backend/docs/viajes.md), [identidad.md](../backend/docs/identidad.md) |
| Catálogos | [catalogos.md](../backend/docs/catalogos.md) |
| Evidencia inmutable y seguridad | [evidencia-inmutable.md](../backend/docs/evidencia-inmutable.md), [seguridad.md](../backend/docs/seguridad.md) |
| Configuración, API y arquitectura | [configuracion.md](../backend/docs/configuracion.md), [api.md](../backend/docs/api.md), [arquitectura.md](../backend/docs/arquitectura.md) |
