# Matriz de trazabilidad — decisión → HU → prueba → estado

Construida para S5-HU-26.1. El milestone original citaba "la tabla de brechas con sus 27 decisiones" en
Notion; esa página solo contiene la narrativa genérica de la historia (objetivo, escenarios Gherkin), no una
tabla real de filas. Esta matriz la reemplaza reconstruyendo el contenido real a partir de las issues
cerradas de cada HU y las pruebas automatizadas que efectivamente las cubren hoy en el repo.

**Cómo leerla:** cada fila es una decisión o regla de negocio verificable. "Prueba" nombra el archivo (y,
cuando aporta, el método) que la cubre. "Estado" es uno de:
- **Verificado con prueba real** — existe una prueba automatizada (unitaria, integración o mobile) que la
  ejerce.
- **Cerrada sin prueba automatizada** — la issue está cerrada pero no hay una prueba que la respalde (incluye
  tareas que por naturaleza no son testeables: scripts SQL de una sola corrida, runbooks, saneamiento puntual).
- **Abierta/pendiente** — la issue sigue abierta en GitHub.

Mantener este archivo significa: al cerrar una HU nueva, agregar su fila aquí con la prueba que la cubre; al
reabrir un bug sobre algo ya marcado "Verificado", corregir su estado y anotar la issue de reapertura.

---

## S5-HU-01 a S5-HU-09 — Saneamiento, caracterización y bootstrap

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-01 | Sanear credenciales embebidas en el repo anterior | — | Cerrada sin prueba automatizada (auditoría puntual, no aplica) |
| S5-HU-02 | Revisar y rotar credenciales de desarrollo que pudieron llegar a producción | — | Cerrada sin prueba automatizada (auditoría puntual) |
| S5-HU-03 | Purgar datos reales versionados del historial | — | Cerrada sin prueba automatizada (auditoría puntual) |
| S5-HU-04 | Documentar la deriva entre el esquema real y las migraciones | — | Cerrada sin prueba automatizada (es un documento) |
| S5-HU-05/06 | Caracterizar la API anterior y escribir casos de línea base para los módulos sin cobertura | `backend/tests/IA-AUTOS.Tests.Integracion/Api/*.cs` (suite completa, por módulo) | Verificado con prueba real |
| S5-HU-07 | Bootstrap .NET 10: capas con regla de dependencias, `/health` | `TransversalesApiTests.cs` (pruebas de `/health`) | Verificado con prueba real |
| S5-HU-08 | Docker no-root, compose dev/QA, `.env.example` sin valores reales | — | Cerrada sin prueba automatizada (infraestructura, no aplica) |
| S5-HU-09 | CI: build + pruebas con Testcontainers | `.github/workflows/ci-backend.yml` | Cerrada, verificación indirecta (el propio pipeline corre la suite) |

## S5-HU-10 — Reportes, Vehículos, Sofasa

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-10.1 | Migrar `/patrones`, `/patrones/analisis`, `/metricas/rechazo` | `backend/tests/IA-AUTOS.Tests.Unitarios/Reportes/ReportesYPdfTests.cs` | Verificado con prueba real |
| S5-HU-10.2 | Migrar `/vehiculos`, `/listado`, `/:vin/inventario` y su PDF | `VehiculosApiTests.cs` | Verificado con prueba real |
| S5-HU-10.3 | Migrar `GET /api/sofasa/ordenes` | `SofasaApiTests.El_listado_requiere_usuario_filtra_y_ordena_las_mas_recientes_primero_con_nulos_al_final` | Verificado con prueba real |

## S5-HU-11 — Auth y Usuarios

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-11.1 | Login con claims compatibles con el token anterior | `AuthApiTests.cs` (suite completa) | Verificado con prueba real |
| S5-HU-11.2 | Jerarquía real de roles en rutas de administración | `IdentidadApiTests.Solo_el_super_administrador_asigna_el_nivel_super_administrador` | Verificado con prueba real |
| S5-HU-11.3 | Rate limiting en login | `SesionPortalApiTests.Los_permisos_que_manda_el_cliente_no_dan_acceso` y suite de `AuthApiTests` | Verificado con prueba real |
| S5-HU-11.4 | CRUD de usuarios con baja lógica/física según tenga historial | `UsuariosApiTests.cs` (altas/bajas) | Verificado con prueba real |

## S5-HU-12 — Archivos

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-12.1 | Almacenamiento S3/MinIO; `/api/uploads/:file` deja de ser pública | `ArchivosApiTests.Con_s3_configurado_crea_el_bucket_guarda_en_almacen_y_sirve_la_imagen_por_la_api` | Verificado con prueba real |
| S5-HU-12.2 | Validación por magic bytes y límite de tamaño, no por extensión declarada | `ArchivosApiTests.Rechaza_contenido_que_no_es_jpeg_ni_png_o_que_no_es_base64`, `Rechaza_imagenes_que_exceden_el_tamano_maximo_configurado`, `Acepta_el_prefijo_data_uri_y_decide_el_tipo_por_el_contenido_no_por_la_extension_declarada` | Verificado con prueba real |

## S5-HU-13 — Inspecciones y Custodia (núcleo, mayor riesgo — ver brechas)

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-13.1 | Reglas de dominio del modelo de inspección (cierre, inmutabilidad, hallazgos) | `backend/tests/IA-AUTOS.Tests.Unitarios/Dominio/InspeccionesTests.cs` (17 métodos) | Verificado con prueba real |
| S5-HU-13.2 | Cerrar exige foto propia en cada hallazgo y ubicación en toda foto | `InspeccionesTests.Cerrar_exige_foto_propia_en_cada_hallazgo`, `InspeccionesTests.Cerrar_exige_ubicacion_en_toda_foto` | Verificado con prueba real |
| **S5-HU-13.3** | Verificar identidad real de quien firma la transferencia (nadie firma por otro) | Ver **Brecha 1** abajo: se cerró como corregida sin prueba suficiente; QA la reabrió de facto (issue #121) y se corrigió hasta S5-HU-25.2 (PR #147) | **Cerrada sin prueba suficiente en su momento → corregida en S5-HU-25.2** |
| S5-HU-13.4 | Triggers de inmutabilidad a nivel de base de datos | `backend/tests/IA-AUTOS.Tests.Integracion/Infraestructura/PersistenciaTests.cs` | Verificado con prueba real |
| S5-HU-13.5 | Escritura real de `tblAuditLog` en acciones sensibles | `CatalogosApiTests.Cada_cambio_deja_rastro_en_la_auditoria` | Verificado con prueba real |

## S5-HU-14 a S5-HU-19 — Migración de datos y corte (ETL, en su mayoría scripts de una sola corrida)

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-14.1 | Carga masiva idempotente (`MERGE`+`HOLDLOCK`) de VIN/etapas legado | Cobertura parcial vía `MigracionViajesTests.cs`; no hay test específico del `MERGE` legado | Cerrada sin prueba automatizada directa (parcial) |
| S5-HU-14.2 | Worker de ingesta Sofasa con marca de agua incremental | `SofasaApiTests.Una_orden_de_un_vin_que_ya_esta_en_un_viaje_queda_vinculada_a_ese_viaje` cubre la idempotencia por API, no el worker en sí | Cerrada sin prueba automatizada directa del worker |
| S5-HU-15 a S5-HU-19 | Ventana de mantenimiento, retiro del sistema legado, script de migración Postgres→SQL Server, estrategia de corte | Documentos y scripts SQL de una sola ejecución | Cerrada sin prueba automatizada (no aplica por naturaleza) |

## S5-HU-20 — Sesión del portal / SSO

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-20.1 | `PortalUsuarioId` único, admite nulos, no relinkea por correo | `SesionPortalApiTests.Un_id_inexistente_en_el_portal_recibe_403...`, `Cambio_de_usuario_del_portal_con_el_mismo_correo_no_relinkea_la_cuenta` | Verificado con prueba real |
| S5-HU-20.2 | Contrato `POST /api/auth/login-portal` → `{access, refresh, user}` | `SesionPortalApiTests.Login_portal_entra_sin_segundo_login_y_responde_access_refresh_y_user` | Verificado con prueba real |
| S5-HU-20.3 | Verificación del id del portal contra la base de TI | `SesionPortalApiTests.Sin_base_de_TI_configurada_solo_entran_los_ids_ya_vinculados` | Verificado con prueba real |
| S5-HU-20.7 | Permisos falsificados ignorados; doble refresh concurrente y reúso de refresh revocan la sesión; logout real | `SesionPortalApiTests.Los_permisos_que_manda_el_cliente_no_dan_acceso`, `Doble_refresh_concurrente_con_el_mismo_token_no_revoca_la_sesion_ganadora`, `Reusar_un_refresh_revoca_toda_la_sesion_incluido_el_access_vigente` | Verificado con prueba real |

## S5-HU-21 — Catálogos maestros administrables

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-21.1–21.4 | Catálogos de etapas, clases de daño y vistas de foto (9 vistas, 7 obligatorias) con seeds | `CatalogosApiTests.Los_catalogos_iniciales_estan_completos`; migración en `MigracionCatalogosMaestrosTests.cs` | Verificado con prueba real |
| S5-HU-21.5 | `GET /api/catalogos` versionado con ETag / 304 | `CatalogosApiTests.La_version_viaja_en_ETag_y_un_If_None_Match_vigente_responde_304` | Verificado con prueba real |
| S5-HU-21.6 | CRUD por catálogo con roles; un valor en uso se inactiva, no se borra | `CatalogosApiTests.Un_valor_en_uso_no_se_borra_responde_409_y_solo_puede_inactivarse` | Verificado con prueba real |
| S5-HU-21.7 | Reemplazar constantes hardcodeadas por catálogos en BD con caché | `CatalogosApiTests.Cada_catalogo_tiene_su_propia_version_que_solo_cambia_si_cambia_su_contenido` | Verificado con prueba real |
| S5-HU-21.13 | `GET /api/catalogos/cuadrantes` con geometría versionada | `CatalogosApiTests.Un_cuadrante_es_una_zona_y_sus_poligonos_se_sirven_aparte_con_su_version` | Verificado con prueba real |
| S5-HU-21.9/21.10/21.12/21.14 | Cliente de catálogos con caché Drift en mobile; geometría de 38 cuadrantes como fuente única; catálogo real de códigos Sofasa | — | **Abierta/pendiente** (issues #81, #83, #85) |

## S5-HU-22 — El viaje como eje y visibilidad por asignación

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-22.1 | `tblViajes`/`tblViajeVehiculos`, etapas e inspecciones referencian el viaje | `MigracionViajesTests.cs`, `PersistenciaTests.cs` | Verificado con prueba real |
| S5-HU-22.2 | Vista `vwViajesCompletos` reemplaza `vwVehiculosMovilizados` | `ViajesApiTests.Un_viaje_completo_es_aquel_cuyos_vin_tienen_todas_sus_etapas_cerradas_y_los_listados_lo_usan` | Verificado con prueba real |
| S5-HU-22.3 | CRUD de viajes, asignación de VIN, carga masiva por Excel | `ViajesApiTests.La_carga_por_excel_muestra_el_error_de_cada_fila_antes_de_confirmar_y_solo_guarda_las_validas` | Verificado con prueba real |
| S5-HU-22.4 | Creación automática de las 3 etapas por VIN con origen/destino | `ViajesApiTests.Crear_un_viaje_crea_las_tres_etapas_de_cada_vin_con_origen_y_destino` | Verificado con prueba real |
| S5-HU-22.5/22.10 | Visibilidad por asignación; 403 en viaje/VIN ajeno; vínculo Sofasa↔viaje | `ViajesApiTests.El_detalle_de_un_viaje_ajeno_es_403_el_propio_se_ve_y_uno_inexistente_es_404`; `SofasaApiTests.Una_orden_de_un_vin_que_ya_esta_en_un_viaje_queda_vinculada_a_ese_viaje` | Verificado con prueba real |
| S5-HU-22.8/22.9 | "Mis viajes" como pantalla principal en mobile; captura toma datos del viaje | `mobile/test/widgets/captura_page_test.dart` (los cuatro datos salen del viaje y no quedan campos escribibles), `mobile/test/viajes/` (repositorio y búsqueda), `mobile/test/vehiculos/inventario_test.dart` (`lugar` y `viaje_id`), contrato atado a `/api/viajes/*` | Implementado en `S5-HU-21-23` y **verificado en dispositivo** con la API local; falta prueba de widget de `MisViajesPage`/`BuscarVinPage` (#91) |
| S5-HU-22.5b (#148) | Filtro por cliente en `GET /api/viajes` y lista de clientes por viaje | — | **Abierta/pendiente** (#148, rol Backend) |

## S5-HU-23 — Identidad: cédula y PIN, clientes y permisos por rol

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-23.1 | `tblUsuarios`: cédula única, hash de PIN, bloqueo por intentos | `MigracionIdentidadTests.cs`; `IdentidadApiTests.Cinco_pines_errados_bloquean_la_cuenta_quince_minutos_y_los_intentos_quedan_en_la_auditoria` | Verificado con prueba real |
| S5-HU-23.2 | `tblClientes`, `ClienteId` en usuarios y vehículos | `IdentidadApiTests.Cada_usuario_ve_y_administra_solo_a_los_de_su_cliente` | Verificado con prueba real |
| S5-HU-23.3 | `POST /api/auth/login-conductor` (cédula+PIN), scrypt, bloqueo | `IdentidadApiTests.Cedula_y_pin_correctos_dan_un_token_de_conductor_que_sirve_en_la_api` | Verificado con prueba real |
| S5-HU-23.4 | Gestión de PIN por rol; `GET /api/auth/me` con capacidades | `IdentidadApiTests.El_administrador_asigna_reinicia_y_fuerza_el_cambio_de_pin` | Verificado con prueba real |
| S5-HU-23.5 | Gestión de usuarios (admin/coordinador), carga masiva de conductores | `IdentidadApiTests.La_vista_previa_de_conductores_no_guarda_y_la_confirmacion_crea_los_validos_con_su_pin` | Verificado con prueba real |
| S5-HU-23.8/23.9 | Mobile: login con dos modos y cambio de PIN forzado; router/menú por rol; cola etiquetada por usuario | `login_page_test.dart`, `login_pin_test.dart` y `auth_controller_test.dart` (login, bloqueo, cambio de PIN y 403 a mitad de sesión); `capacidades_test.dart` (capacidades y su respaldo por rol); `menu_por_rol_test.dart` (menú y rutas sin permiso); `cola_por_usuario_test.dart` (celular compartido y huérfanos); `cambiar_pin_page_test.dart` (la pantalla y el redirect del router) | 23.8 y 23.9 implementadas en `S5-HU-21-23`; 23.8 **verificada en dispositivo**, 23.9 pendiente de probar en dispositivo |

## S5-HU-24 — Inspección de salida y llegada con 7 fotos obligatorias y 2 adicionales

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-24.1 | `tblEvidencias`: vista, hash SHA-256, sello del servidor, GPS obligatorio | `MigracionInspeccionesTests.cs` | Verificado con prueba real |
| S5-HU-24.2 | `momento` restringido a salida/llegada; versión y corrección referenciada | `InspeccionesApiTests.Los_momentos_retirados_ya_no_se_aceptan`, `Corregir_crea_una_version_nueva_que_referencia_a_la_anterior_y_la_anterior_no_cambia` | Verificado con prueba real (ver también §PR #146 abajo) |
| S5-HU-24.4 | 409 `ETAPA_PREVIA_SIN_CIERRE` si la llegada anterior no está cerrada | `InspeccionesApiTests.La_salida_exige_que_la_llegada_de_la_etapa_anterior_este_cerrada` | Verificado con prueba real |
| S5-HU-24.5 | Evidencias con vista/nota/hash/sello; `POST /api/uploads` multipart | `ArchivosApiTests.cs` (suite de subida) | Verificado con prueba real |
| S5-HU-24.6/24.7 | Checklist de vistas (7+2) y reglas de cierre en mobile (`reglas_informe.dart`) | `mobile/test/inspecciones/reglas_informe_test.dart` (grupos `bloqueoDeCierre`, `construirInformeJob`, `firmaValida`) | Verificado con prueba real |
| S5-HU-24.8/24.9/24.11/24.12 | Compresión y envío multipart; tarjetas por etapa; pruebas de campo en dispositivos; pantallas de captura | — | **Abierta/pendiente** (#107–#112) |

## S5-HU-25 — Transferencia de custodia: firmante real, recibo con reservas, constancia de no firma

| ID | Decisión/regla | Prueba | Estado |
|---|---|---|---|
| S5-HU-25.1 | `tblTransferenciasCustodia`: tipo de cierre, reservas, motivo de no firma, testigo, fecha e IP por firma | `MigracionTransferenciasTests.cs`; `CustodiaYUsuariosTests.Crear_exige_usuarios_distintos_y_nace_abierta_sin_firmas` | Verificado con prueba real (PR #147) |
| **S5-HU-25.2** | **Cada firma la envía su titular** — corrige el bug de la issue #121 (el servidor aceptaba cualquier id de usuario que mandara el cliente, sin validar contra el token) | Dominio: `CustodiaYUsuariosTests.Nadie_firma_por_otro`. Integración (QA, verificado en vivo contra SQL Server real el 2026-10-16): `InspeccionesApiTests.Nadie_firma_por_otro_ni_crea_una_transferencia_ajena`, `InspeccionesApiTests.Cada_uno_firma_su_parte_aunque_envie_los_dos_ids_y_los_firmantes_no_se_pueden_cambiar` | **Verificado con prueba real — issue #121 cerrada** |
| S5-HU-25.3 | Recibo con reservas (observaciones obligatorias) y constancia de no firma (motivo, testigo, foto); PDF de constancia | `CustodiaYUsuariosTests.Recibir_con_reservas_exige_las_observaciones`, `La_no_firma_exige_motivo_testigo_y_foto_y_cierra_la_transferencia`; `GeneradorPdfConstanciaTests.cs` | Verificado con prueba real |
| S5-HU-25.4/25.5/25.7 | Firmantes del viaje, firma por otro bloqueada (403), reservas sin texto rechazadas, no firma sin testigo rechazada, bloqueo de la etapa siguiente sin cierre | `CustodiaYUsuariosTests.Mientras_esta_abierta_cada_uno_puede_repetir_su_propia_firma`, `El_testigo_es_una_tercera_persona`; `InspeccionesApiTests.Una_transferencia_sin_cierre_bloquea_la_salida_de_la_etapa_siguiente_hasta_que_se_cierra` | Verificado con prueba real (dominio y API); falta la capa de UI en mobile (**Abierta/pendiente** #116, #117, #118) |
| #151 | Mobile sigue enviando `firma_entrega`/`firma_recibe` (contrato anterior a S5-HU-25.2) | Sin prueba — contrato roto, issue abierta para el equipo móvil | **Abierta/pendiente** |

---

## Brechas detectadas

1. **S5-HU-13.3 (firma de transferencia) — la más seria.** Se había cerrado como corregida en el sistema
   anterior sin una prueba de regresión que lo respaldara. QA la verificó de nuevo el 2026-10-06 contra `main`
   (issue #121) y confirmó que el servidor seguía aceptando los ids de usuario que mandara el cliente, sin
   validar contra el token: cualquiera podía firmar por otro. Quedó corregida de raíz hasta S5-HU-25.2 (PR
   #147, verificada en vivo el 2026-10-16: ver fila correspondiente). **Lección:** "cerrado en GitHub" no es
   evidencia de que la regla esté implementada; toda decisión de seguridad/integridad debería tener su prueba
   de regresión antes de cerrarse, no solo la palabra del PR.

2. **Contrato roto en mobile tras S5-HU-25 (issue #151, abierta).** El cambio de contrato que corrigió la
   brecha anterior (de `firma_entrega`/`firma_recibe` a `firma`) no se propagó a la app móvil
   (`gateway_informes_api.dart`), que seguirá enviando el payload viejo. Riesgo real si el backend de `QA`/`main`
   se despliega antes de que mobile actualice su cliente.

3. **Catálogos en mobile con flecos abiertos (#81, #83, #85).** El backend ya sirve el cliente de catálogos
   versionado, la geometría de cuadrantes y los códigos Sofasa, pero mobile mantiene listas hardcodeadas en
   paralelo (`selector_dano.dart`, `etiquetas.dart`) en vez de consumir la API.

4. **UX de viajes pendiente en mobile (#148; #91 y #92 implementados en `S5-HU-21-23`).** El modelo de viaje
   como eje está completo en backend, y mobile ya entra por "Mis viajes" y toma los datos del viaje en la
   captura. Queda el filtro por cliente (#148). Dos puntos a revisar al retomar #91:
   - `MisViajesPage` y `BuscarVinPage` no tienen prueba de widget: los cuatro estados (cargando, error, sin
     conexión, sin viajes), el contenido de la tarjeta y que tocar un VIN navegue solo se han comprobado a mano.
     La captura sí quedó cubierta (`captura_page_test.dart`).
   - `ViajesRepositorio.deLaEtapa` sirve la copia guardada y **no la revalida** aunque haya señal: si el
     coordinador corrige la placa después de la descarga, la captura muestra la vieja. Es informativo —el
     servidor toma el dato real del viaje al recibir el informe— y leer primero la copia es lo que hace que la
     captura funcione sin señal. El arreglo correcto es servir y refrescar después, como los catálogos.

5. **Identidad en mobile: #99 y #100 implementadas en `S5-HU-21-23`.** Login con cédula y PIN, cambio forzado,
   403 `PIN_CHANGE_REQUIRED` a mitad de sesión, menú y rutas por capacidades, y cola etiquetada por usuario. Lo
   que queda anotado:
   - **La pestaña Sofasa pide `gestionar_viajes` por decisión de la app**, no del servidor: el endpoint
     `GET /api/sofasa/ordenes` no restringe por rol y ninguna capacidad habla de esa pantalla. Se eligió así
     porque son las órdenes de carga de la planta, insumo de quien arma viajes. Si se define otra cosa, se
     cambia en `mobile/lib/app/pestanas.dart`.
   - **Las capacidades no se refrescan con la sesión ya abierta**: si a alguien le cambian el rol, su menú
     sigue igual hasta el siguiente ingreso. El daño está acotado —la API revalida el rol en cada petición, así
     que una pestaña de más termina en «sin permisos» o en 403—, pero conviene revisarlo si aparece el caso.
   - **Verificado en dispositivo (23-oct-2026):** la coordinadora ve Mis viajes, Inspecciones y Sofasa; la
     inspectora y el conductor, solo las dos primeras. El menú se arma con lo que responde `/api/auth/me`.
   - **Abierto, para revisar con el equipo: qué hace un coordinador en la app de campo.** Entra y ve tres
     pestañas vacías, y es correcto: `GET /api/viajes/mios` devuelve los viajes donde se participa como
     conductor o inspector, y un coordinador no participa en ninguno (comprobado: `/mios` responde `[]` y
     `/api/viajes` sí trae el viaje). Ninguna HU dice qué hace ese rol en el móvil; los viajes se administran
     desde el portal. Las salidas posibles son dejarlo así, pedirle `registrar_inspecciones` a la pestaña «Mis
     viajes» para que no la vea, o mostrarle el listado completo con `GET /api/viajes`, que es funcionalidad
     nueva. Es decisión de producto, no de la app.

6. **Inspecciones: UI y pruebas de campo pendientes (#107–#112).** La lógica de dominio y las reglas de cierre
   tienen prueba automatizada tanto en backend como en `reglas_informe_test.dart`; falta la capa de pantallas y
   la validación de campo en dos dispositivos físicos mencionada explícitamente en #111.

7. **Transferencia de custodia: UI pendiente (#116, #117, #118).** La base de datos y los endpoints troncales
   (25.1–25.3) están cerrados y probados; los flujos de usuario en mobile (firmar desde la sesión propia,
   reservas, no firma) siguen abiertos.

8. **Cobertura parcial del ETL legado (S5-HU-14).** No se encontró una prueba automatizada específica para el
   `MERGE`+`HOLDLOCK` de VIN/etapas del sistema anterior ni para el `BackgroundService` de ingesta de Sofasa en
   sí mismo (solo se prueba su idempotencia a través de la API).

## Notas de alcance

- No se buscó llegar a "27 filas" exactas: ese número era parte de la narrativa original de la historia en
  Notion, no una lista real que exista en algún lado. Esta matriz tiene más filas porque agrupa por sub-HU
  donde hay una regla de negocio verificable de forma independiente.
- Los nombres de archivo y de método citados se confirmaron contra el código real del repo (rama `QA`) antes
  de publicar este documento — no se tomó ningún nombre por buena fe de una primera pasada.
- Mantenimiento: al cerrar una HU nueva, agregar su fila; al reabrir algo marcado "Verificado", mover su fila a
  "Brechas detectadas" con la issue de reapertura, como se hizo aquí con S5-HU-13.3 / #121.
