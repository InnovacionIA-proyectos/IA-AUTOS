import '../../../core/errores/error_api.dart';
import '../../viajes/domain/entrega_punto.dart';
import 'desenlace_envio.dart';
import 'job.dart';
import 'puertos.dart';

/// Códigos con que el servidor explica un 409 del conductor en ruta.
const codigoEtapaPreviaSinCierre = 'ETAPA_PREVIA_SIN_CIERRE';
const codigoLlegadaYaCerrada = 'LLEGADA_YA_CERRADA';
const codigoUnidadNoSale = 'UNIDAD_NO_SALE';

/// Lo que dice un envío retirado de un carro que se bajó del viaje. El de un carro que no sale es el del servidor.
const mensajeCarroBajado = 'El carro ya no está en el viaje';

/// Lo que dice un informe de salida del descargue encolado antes de que el destino tuviera una sola revisión
/// (S8-HU-06.2 CA-5): el servidor ya no admite ese momento.
const mensajeSalidaDescargue = 'En el destino solo se revisa la llegada: este informe ya no aplica';
const codigoMomentoNoAdmitido = 'MOMENTO_NO_ADMITIDO';

/// El `details.motivo` de ese 409: la salida del descargue, que ya no existe (S8-HU-06.2).
const motivoSalidaDescargue = 'salida_descargue';

/// Por qué el servidor ya no admite este envío del carro, o null si el rechazo es otro (S8-HU-02.2 CA-4 y CA-6).
///
/// Llega de tres maneras: las inspecciones y las firmas responden 409 con `details.motivo` `unidad_no_sale` o
/// `unidad_bajada`; la novedad y el «Llegué» del conductor, 409 con el código `UNIDAD_NO_SALE` (también cuando el
/// carro se bajó: el servidor responde lo mismo).
String? motivoDeRetiro(ErrorApi e, Job job) {
  final motivo = switch (e.details) { final Map m => m['motivo'], _ => null };
  if (e.status == 409 && (motivo == 'unidad_no_sale' || e.code == codigoUnidadNoSale)) return e.message;
  if (e.status == 409 && motivo == 'unidad_bajada') return mensajeCarroBajado;
  // S8-HU-01.3 CA-11: en el cargue ya no se firma carro por carro; un envío viejo se retira con lo que dice el
  // servidor («firma desde la pantalla de la niñera»), no queda «con error».
  if (e.status == 409 && motivo == 'firma_por_ninera') return e.message;
  // S8-HU-04.2 CA-11: en el destino tampoco; la firma de un carro del descargue encolada antes se retira con lo que
  // dice el servidor («usa Entregar en esta parada»).
  if (e.status == 409 && motivo == 'entrega_por_parada') return e.message;
  // S8-HU-06.2 CA-5: la salida del descargue ya no existe. Reintentarla daría el mismo 409 para siempre.
  if (e.status == 409 && (motivo == motivoSalidaDescargue || e.code == codigoMomentoNoAdmitido)) return mensajeSalidaDescargue;
  // S8-HU-07.3 CA-8: el informe de transporte ya lo arma el sistema con «Reportar novedad» y «Llegué»; uno manual
  // encolado antes no se reintenta: se retira con lo que dice el servidor.
  if (e.status == 409 && motivo == motivoTransporteLoArmaElSistema) return e.message;
  // S8-HU-07.3 CA-6: el lugar de cargue no admite cargue sin inspector. Reintentar daría el mismo 409: el cargue lo
  // hace el inspector, y se dice con el mensaje del servidor en vez de quedar «con error».
  if (e.status == 409 && motivo == motivoLugarNoAdmiteSinInspector) return e.message;
  return null;
}

/// Los motivos (`details.motivo`) con que el servidor rechaza lo que la app ya no ofrece (S8-HU-07.3).
const motivoTransporteLoArmaElSistema = 'transporte_lo_arma_el_sistema';
const motivoLugarNoAdmiteSinInspector = 'lugar_no_admite_cargue_sin_inspector';
const motivoConductorSinPermiso = 'conductor_sin_permiso_de_excepcion';

/// El motivo del 409 `ETAPA_PREVIA_SIN_CIERRE` cuando lo que falta es la entrega (la transferencia) de la etapa anterior.
const motivoTransferenciaSinCierre = 'transferencia_sin_cierre';

/// Lo que dice un envío del conductor en ruta que espera a que se cierre la entrega del cargue (S8-HU-07.3 CA-5).
const mensajeEntregaDelCargueSinCerrar = 'Falta cerrar la entrega del cargue';

/// El código con que se guarda un rechazo en el job. Casi siempre es el del servidor; cambia donde ese código haría
/// que la cola lo tratara como lo que no es:
///
/// - la entrega de una parada guarda el motivo de la parada (S8-HU-04.2 CA-3);
/// - un 403 que no es «lo empezó otra persona» no se guarda como `FORBIDDEN`: la cola lo mostraría como «De otro
///   usuario» y no dejaría ni reintentarlo ni quitarlo. Son la llegada del cargue sin el permiso concedido (S8-HU-07.3
///   CA-7: queda con el mensaje del servidor y con su motivo como código) y cualquier rechazo de la novedad del
///   viaje, que solo la envía su dueño (CA-3: queda en «Cola» con el mensaje del servidor).
String codigoDelRechazo(ErrorApi e, Job job) {
  if (job is EntregaPuntoJob || job is SinInspectorParadaJob) return motivoDeLaParada(e) ?? e.code;
  final motivo = switch (e.details) { final Map m => m['motivo'], _ => null };
  if (e.status == 403 && motivo == motivoConductorSinPermiso) return motivoConductorSinPermiso;
  if (job is NovedadViajeJob && e.code == codigoProhibido) return motivo is String ? motivo : codigoNovedadViajeRechazada;
  return e.code;
}

/// Código local de una novedad del viaje que el servidor rechazó por permisos sin decir el motivo.
const codigoNovedadViajeRechazada = 'NOVEDAD_VIAJE_RECHAZADA';

/// El motivo con que el servidor rechaza el cargue del conductor sin la excepción «No hay inspector» activa (`403`).
/// Es el mismo para «pida el permiso … y espere» que para rechazos que el tiempo no arregla (una llegada sin motivo
/// donde no hay inspectores, o sin ningún permiso pedido).
const motivoEsperaDelPermiso = motivoConductorSinPermiso;

/// Si el servidor rechazó [job] solo porque el permiso «No hay inspector» que se pidió todavía no se ve concedido
/// (S8-HU-09.2 CA-2).
///
/// Con la cuenta atrás en cero la app deja seguir sin esperar a ver el permiso concedido, porque el servidor lo
/// concede en la misma petición (ESC-03). Si el reloj del teléfono iba adelantado y el servidor aún responde que
/// espere, eso no es un rechazo definitivo: en segundos o minutos el permiso se concede, por tiempo o por un
/// coordinador. Por eso no se marca «prohibido» —que solo su dueño arreglaría y nadie podría reintentar—: el informe
/// se queda en la cola, con lo capturado, y vuelve a salir solo.
///
/// Solo si el informe se capturó siguiendo un permiso pedido ([InformeJob.bajoPermiso]): el servidor usa el mismo
/// motivo cuando no hay ningún permiso que esperar, y reintentar eso media hora no cambia nada.
bool esEsperaDelPermiso(ErrorApi e, Job job) =>
    job is InformeJob &&
    job.bajoPermiso &&
    e.status == 403 &&
    switch (e.details) { final Map m => m['motivo'] == motivoEsperaDelPermiso, _ => false };


/// El motivo de la parada con que el servidor rechazó una entrega (S8-HU-04.2 CA-3) o «No hay inspector» (S8-HU-05.2
/// CA-6), o null si el rechazo es otro.
///
/// Se guarda como código del job en vez del código genérico del error: la pantalla de la parada decide con él qué
/// explicar, sin leer el texto. Así un 403 `entrega_tomada` tampoco queda como «prohibido»: no es algo que solo su
/// dueño pueda arreglar, y quien lo ve en la cola lo puede quitar.
String? motivoDeLaParada(ErrorApi e) {
  final motivo = switch (e.details) { final Map m => m['motivo'], _ => null };
  return motivo is String && MotivoParada.todos.contains(motivo) ? motivo : null;
}

enum _Desenlace { hecho, espera }

/// Lo que responde el servidor a la firma de un punto que ya no tiene unidades por entregar.
const mensajeSinPendientes = 'No hay unidades pendientes de entrega';

/// Resultado de una pasada de sincronización.
class ResultadoSync {
  const ResultadoSync({
    required this.enviados,
    required this.pendientes,
    required this.fallidos,
    this.retirados = 0,
    this.pausa,
  });

  final int enviados;

  /// Jobs que todavía se reintentarán solos.
  final int pendientes;

  /// Jobs que necesitan atención del usuario (reintento manual o descarte).
  final int fallidos;

  /// Jobs que el servidor ya no admite porque el carro no sale o se bajó del viaje. No son un error, pero quien
  /// los encoló tiene que poder enterarse de que no se enviaron y por qué.
  final int retirados;

  /// Null si la pasada sí recorrió la cola. Con valor, no se intentó nada y por eso ningún job quedó con motivo.
  final MotivoPausa? pausa;
}

/// Reproduce la cola contra la API respetando la semántica de la app original:
///
///  - FIFO y un job a la vez: se respeta el orden de las etapas.
///  - Se re-valida la señal ANTES de cada job; si se cae, se detiene y se reanuda luego.
///  - Cada paso completado se persiste (reanudable): no duplica inspecciones, fotos ni hallazgos.
///  - Hasta [maxIntentos] intentos. 401, 408, 429, 5xx y errores de red son transitorios: el job
///    queda en la cola y se detiene la pasada para no romper el orden.
///  - Un error no recuperable (400, 403, 404...) o agotar los intentos marca el job como FALLIDO:
///    no bloquea a los demás, pero se conserva y se muestra al usuario (a diferencia de descartarlo).
///  - 409 al cerrar una inspección significa "ya estaba cerrada" y cuenta como éxito.
///  - Solo se procesa lo de [usuarioId]: la cola sobrevive al cierre de sesión y el servidor atribuye el informe
///    al dueño del token, así que en un equipo compartido lo de una persona saldría a nombre de la siguiente.
class ProcesadorCola {
  ProcesadorCola({
    required this.cola,
    required this.gateway,
    required this.red,
    required this.archivos,
    this.maxIntentos = 8,
  });

  final ColaRepositorio cola;
  final GatewayInformes gateway;
  final EstadoRed red;
  final ArchivosLocales archivos;
  final int maxIntentos;

  bool _procesando = false;

  /// Lo que esta persona tiene en la cola. Lo de otras no se cuenta ni se envía; los huérfanos (encolados por una
  /// versión anterior, sin dueño) se cuentan para que se vean, pero nunca se envían solos.
  Future<List<Job>> _suyos(String usuarioId) async =>
      (await cola.listar()).where((j) => j.esDe(usuarioId)).toList();

  /// Jobs sobre los que esta persona puede actuar: los suyos y los huérfanos. Son los que se cuentan en la
  /// insignia, porque son el trabajo que le queda por delante. Los de otra persona se ven en la pantalla de la
  /// cola, pero no son suyos ni para enviar ni para contar.
  Future<List<Job>> visibles(String usuarioId) async =>
      (await cola.listar()).where((j) => j.esDe(usuarioId) || j.esHuerfano).toList();

  /// Todo lo que hay en el dispositivo, para la pantalla de la cola: lo de esta persona y lo que quedó de otras,
  /// que tiene que verse —si no, quien usa el equipo no entiende por qué hay envíos que nunca salen—.
  Future<List<Job>> enElDispositivo() => cola.listar();

  /// Cantidad de jobs que aún se reintentarán solos.
  Future<int> contarPendientes(String usuarioId) async => (await visibles(usuarioId)).where((j) => !j.fallido).length;

  Future<int> contarFallidos(String usuarioId) async =>
      (await visibles(usuarioId)).where((j) => j.fallido && !j.retirado).length;

  Future<int> contarRetirados(String usuarioId) async => (await visibles(usuarioId)).where((j) => j.retirado).length;

  /// Adopta los jobs sin dueño para [usuarioId]. Se llama al arrancar con una sesión guardada: esa es, casi con
  /// certeza, quien los encoló, porque es la última que se usó antes de actualizar la app. Devuelve cuántos.
  Future<int> adoptarHuerfanos(String usuarioId) async {
    final huerfanos = (await cola.listar()).where((j) => j.esHuerfano).toList();
    for (final job in huerfanos) {
      await cola.guardar(job.conUsuario(usuarioId));
    }
    return huerfanos.length;
  }

  Future<ResultadoSync> _resultado(int enviados, String usuarioId, {MotivoPausa? pausa}) async {
    final jobs = await visibles(usuarioId);
    return ResultadoSync(
      enviados: enviados,
      pendientes: jobs.where((j) => !j.fallido).length,
      fallidos: jobs.where((j) => j.fallido && !j.retirado).length,
      retirados: jobs.where((j) => j.retirado).length,
      pausa: pausa,
    );
  }

  Future<ResultadoSync> procesar(String usuarioId) async {
    if (_procesando) return _resultado(0, usuarioId);
    _procesando = true;
    var enviados = 0;
    try {
      // No empezar a drenar si la señal no es estable o la API no responde. Se dice CUÁL de las dos: como no se
      // intenta nada, ningún job queda con motivo, y quien envió algo se quedaba oyendo «se enviará cuando la
      // señal sea estable» con el wifi perfecto y el servidor caído.
      if (!await red.redEstable()) return await _resultado(0, usuarioId, pausa: MotivoPausa.sinSenal);
      if (!await red.apiAlcanzable()) return await _resultado(0, usuarioId, pausa: MotivoPausa.apiCaida);

      final esperando = <String>{};
      while (true) {
        if (!await red.redEstable()) break;
        // Los que esperan a que se cierre la etapa anterior se saltan en esta pasada: no bloquean a los demás.
        final job = (await _suyos(usuarioId)).where((j) => !j.fallido && !esperando.contains(j.id)).firstOrNull;
        if (job == null) break;

        try {
          switch (job) {
            case InformeJob():
              await _ejecutarInforme(job);
            case FirmaJob():
              await _ejecutarFirma(job);
            case NoFirmaJob():
              await _ejecutarNoFirma(job);
            case NotaJob():
              await gateway.agregarNota(entidad: job.entidad, entidadId: job.entidadId, texto: job.texto);
            case EntregaPuntoJob():
              await _ejecutarEntregaPunto(job);
            case SinInspectorParadaJob():
              // S8-HU-05.2 CA-2: reenviarla no crea otra espera; con una abierta el servidor devuelve esa misma.
              await gateway.activarSinInspector(
                job.viajeId,
                job.lugarId,
                motivo: job.motivo,
                motivoTexto: job.motivoTexto,
                fechaMomento: job.fechaMomento,
                lat: job.lat,
                lng: job.lng,
                precisionM: job.precisionM,
              );
            case ReciboSinEntregaJob():
              await gateway.firmarReciboSinEntrega(job.inspeccionId, firma: job.firma);
            case FirmaNineraJob():
              await gateway.firmarCargueNinera(
                job.viajeId,
                parte: job.parte,
                firma: job.firma,
                fechaMomento: job.fechaMomento,
                lat: job.lat,
                lng: job.lng,
                precisionM: job.precisionM,
                reservas: job.reservas,
              );
            case NoFirmaNineraJob():
              await _ejecutarNoFirmaNinera(job);
            case NovedadJob():
              if (await _ejecutarNovedad(job) == _Desenlace.espera) {
                esperando.add(job.id);
                continue;
              }
            case LlegadaJob():
              if (await _ejecutarLlegada(job) == _Desenlace.espera) {
                esperando.add(job.id);
                continue;
              }
            case NovedadViajeJob():
              await _ejecutarNovedadViaje(job);
          }
          await cola.eliminar(job.id);
          enviados++;
        } catch (e) {
          final actual = (await cola.listar()).where((j) => j.id == job.id).firstOrNull;
          if (actual == null) continue;
          final retiro = e is ErrorApi ? motivoDeRetiro(e, actual) : null;
          if (retiro != null) {
            await cola.guardar(actual.conIntento(intentos: actual.intentos + 1, error: retiro, codigo: codigoRetirado, fallido: true));
            continue;
          }
          // S8-HU-09.2 CA-2: el permiso aún no se ve concedido. Queda pendiente (salvo que agote los intentos) y se
          // salta en esta pasada: no bloquea a los demás y vuelve a salir en la siguiente.
          if (e is ErrorApi && esEsperaDelPermiso(e, actual)) {
            final intentos = actual.intentos + 1;
            await cola.guardar(
              actual.conIntento(intentos: intentos, error: e.message, codigo: motivoEsperaDelPermiso, fallido: intentos >= maxIntentos),
            );
            esperando.add(actual.id);
            continue;
          }
          final transitorio = e is! ErrorApi || e.esTransitorio;
          // El PIN temporal no es un fallo del envío: la cola queda en pausa hasta que la persona lo cambie, así
          // que no gasta uno de los ocho intentos. Cualquier otro error sí cuenta.
          final enPausaPorPin = e is ErrorApi && e.esCambioPinRequerido;
          final intentos = enPausaPorPin ? actual.intentos : actual.intentos + 1;
          await cola.guardar(
            actual.conIntento(
              intentos: intentos,
              // Con su detalle: lo que el servidor rechazó, línea por línea, es lo único accionable.
              error: e is ErrorApi ? e.mensajeDetallado : e.toString(),
              codigo: switch (e) {
                // Un fallo pasajero de la parada no es un rechazo: si agota los intentos se dice que no hubo respuesta.
                final ErrorApi api when (actual is EntregaPuntoJob || actual is SinInspectorParadaJob) && motivoDeLaParada(api) == null =>
                  transitorio && !api.esCambioPinRequerido ? codigoSinRespuesta : api.code,
                final ErrorApi api => codigoDelRechazo(api, actual),
                _ => null,
              },
              fallido: !transitorio || intentos >= maxIntentos,
            ),
          );
          if (transitorio && intentos < maxIntentos) break; // reintentar luego, sin romper el orden
          // No recuperable: queda marcado como fallido y se sigue con el siguiente.
        }
      }
    } finally {
      _procesando = false;
    }
    return _resultado(enviados, usuarioId);
  }

  /// Devuelve un job fallido a la cola para que se reintente (acción manual del usuario).
  ///
  /// Quien reintenta se hace cargo: si estaba huérfano pasa a ser suyo, y saldrá con su token. Es un acto
  /// explícito, que es la única forma aceptable de que un informe cambie de autor.
  Future<void> reintentar(String id, String usuarioId) async {
    final job = (await cola.listar()).where((j) => j.id == id).firstOrNull;
    if (job == null) return;
    await cola.guardar(job.conUsuario(usuarioId).conIntento(intentos: 0, error: null, fallido: false));
  }

  /// Descarta un job (acción manual): lo quita de la cola y borra sus fotos locales.
  Future<void> descartar(String id) async {
    final job = (await cola.listar()).where((j) => j.id == id).firstOrNull;
    if (job == null) return;
    if (job is InformeJob) await _borrarFotos(job);
    if (job is EntregaPuntoJob) await _borrarRutas([for (final f in job.fotos) f.ruta]);
    if (job is NovedadViajeJob) await _borrarRutas([?job.foto?.ruta]);
    await cola.eliminar(id);
  }

  Future<void> _ejecutarInforme(InformeJob inicial) async {
    var job = inicial;

    // 1) Inspección (reanudable). Una corrección no crea una inspección nueva en la etapa: pide la versión
    //    siguiente de la que corrige, que nace vacía y en borrador. Se pide AQUÍ y no al pulsar «Corregir»
    //    porque el servidor no deja descartar una versión vacía: una corrección abandonada dejaría la etapa con
    //    un borrador que impide volver a corregirla.
    if (job.inspeccionId == null) {
      final corrige = job.corrigeInspeccionId;
      final id = corrige != null
          ? await gateway.corregirInspeccion(corrige, fecha: job.fecha, lat: job.lat, lng: job.lng, notas: job.notas)
          : await gateway.crearInspeccion(
              etapaId: job.etapaId,
              momento: job.momento,
              fecha: job.fecha,
              lat: job.lat,
              lng: job.lng,
              notas: job.notas,
              motivoSinInspector: job.motivoSinInspector,
              motivoSinInspectorTexto: job.motivoSinInspectorTexto,
            );
      job = job.conProgreso(inspeccionId: id);
      await cola.guardar(job);
    }
    final inspeccionId = job.inspeccionId!;

    // 1b) Posiciones de la niñera (solo la segunda inspección del cargue). Van después de crearla porque el
    //     servidor solo las admite mientras esa inspección está en curso, y antes de cerrar porque el cierre las exige.
    if (!job.posicionesEnviadas && job.viajeId != null && job.posiciones.isNotEmpty) {
      final cambiadas = [
        for (final e in job.posiciones.entries)
          if (job.posicionesPrevias[e.key] != e.value) e,
      ];
      // Primero se liberan las que cambian y después se asignan: un intercambio (la 4 pasa a la 3 y la 3 a la 4)
      // chocaría con la que todavía ocupa el lugar.
      for (final e in cambiadas) {
        await gateway.registrarPosicion(job.viajeId!, e.key, null);
      }
      for (final e in cambiadas) {
        await gateway.registrarPosicion(job.viajeId!, e.key, e.value);
      }
      job = job.conProgreso(posicionesEnviadas: true);
      await cola.guardar(job);
    }

    // 2) Fotos: subir (si falta) y crear la evidencia con el sello PROPIO de cada foto (si falta).
    for (var i = 0; i < job.fotos.length; i++) {
      var foto = job.fotos[i];
      if (foto.storagePath == null) {
        final subido = await gateway.subirImagen(await archivos.leerBytes(foto.ruta), nombre: 'foto-$i.jpg');
        // El hash lo calcula el servidor al recibir el archivo; se guarda y se reenvía con la evidencia, que es
        // como se detecta que el archivo llegó dañado.
        foto = foto.copyWith(storagePath: subido.storagePath, hash: subido.hash);
        job = job.conProgreso(fotos: _reemplazar(job.fotos, i, foto));
        await cola.guardar(job);
      }
      if (!foto.enviada) {
        await gateway.agregarEvidencia(
          inspeccionId,
          storagePath: foto.storagePath!,
          selloFecha: foto.selloFecha,
          selloLat: foto.selloLat,
          selloLng: foto.selloLng,
          vistaId: foto.vistaId,
          precision: foto.precision,
          dispositivoId: foto.dispositivoId,
          nota: foto.nota,
          hash: foto.hash,
        );
        foto = foto.copyWith(enviada: true);
        job = job.conProgreso(fotos: _reemplazar(job.fotos, i, foto));
        await cola.guardar(job);
      }
    }

    // 3) Hallazgos (reanudable por índice). Cada uno va con su foto dentro, así que primero se sube el archivo
    //    y se guarda el progreso: si se corta aquí, al reanudar no se vuelve a subir.
    for (var i = job.hallazgosEnviados; i < job.hallazgos.length; i++) {
      var hallazgo = job.hallazgos[i];
      final foto = hallazgo.foto;
      if (foto != null && foto.storagePath == null) {
        final subido = await gateway.subirImagen(await archivos.leerBytes(foto.ruta), nombre: 'hallazgo-$i.jpg');
        hallazgo = hallazgo.conFoto(foto.copyWith(storagePath: subido.storagePath, hash: subido.hash));
        job = job.conProgreso(hallazgos: _reemplazar(job.hallazgos, i, hallazgo));
        await cola.guardar(job);
      }
      await gateway.agregarHallazgo(inspeccionId, hallazgo);
      job = job.conProgreso(hallazgosEnviados: i + 1);
      await cola.guardar(job);
    }

    // 3b) Extras: se crea cada uno (y se guarda el id que le dio el servidor) y después se mandan sus fotos. Va antes
    //     de cerrar porque el cierre exige la foto de cada extra. Es reanudable por extra y por foto.
    for (var i = 0; i < job.extras.length; i++) {
      var extra = job.extras[i];
      if (extra.extraId == null) {
        final id = await gateway.crearExtra(
          inspeccionId,
          tipo: extra.tipo,
          texto: extra.textoOtro,
          observacion: extra.observacion,
          dano: extra.dano,
        );
        extra = extra.conId(id);
        job = job.conProgreso(extras: _reemplazar(job.extras, i, extra));
        await cola.guardar(job);
      }
      for (var k = 0; k < extra.fotos.length; k++) {
        var foto = extra.fotos[k];
        if (foto.storagePath == null) {
          final subido = await gateway.subirImagen(await archivos.leerBytes(foto.ruta), nombre: 'extra-$i-$k.jpg');
          foto = foto.copyWith(storagePath: subido.storagePath, hash: subido.hash);
          extra = extra.conFotos(_reemplazar(extra.fotos, k, foto));
          job = job.conProgreso(extras: _reemplazar(job.extras, i, extra));
          await cola.guardar(job);
        }
        if (!foto.enviada) {
          await gateway.agregarEvidencia(
            inspeccionId,
            storagePath: foto.storagePath!,
            selloFecha: foto.selloFecha,
            selloLat: foto.selloLat,
            selloLng: foto.selloLng,
            precision: foto.precision,
            dispositivoId: foto.dispositivoId,
            nota: foto.nota,
            hash: foto.hash,
            extraId: extra.extraId,
          );
          foto = foto.copyWith(enviada: true);
          extra = extra.conFotos(_reemplazar(extra.fotos, k, foto));
          job = job.conProgreso(extras: _reemplazar(job.extras, i, extra));
          await cola.guardar(job);
        }
      }
    }

    // 4) Cerrar (idempotente: 409 "ya cerrada" cuenta como éxito).
    if (!job.cerrada) {
      try {
        await gateway.cerrarInspeccion(inspeccionId);
      } on ErrorApi catch (e) {
        // El 409 de un carro que ya no sale o se bajó no es «ya cerrada»: se retira con su explicación.
        if (e.status != 409 || motivoDeRetiro(e, job) != null) rethrow;
      }
      job = job.conProgreso(cerrada: true);
      await cola.guardar(job);
    }

    // 5) Limpiar los archivos locales.
    await _borrarFotos(job);
  }

  /// Como la constancia por carro: la foto se sube primero y queda anotada, así un reintento no la sube dos veces.
  Future<void> _ejecutarNoFirmaNinera(NoFirmaNineraJob job) async {
    var actual = job;
    if (actual.foto.storagePath == null) {
      final subido = await gateway.subirImagen(await archivos.leerBytes(actual.foto.ruta), nombre: 'no-firma-ninera.jpg');
      actual = actual.conFoto(actual.foto.copyWith(storagePath: subido.storagePath, hash: subido.hash));
      await cola.guardar(actual);
    }
    await gateway.registrarNoFirmaNinera(
      actual.viajeId,
      parte: actual.parte,
      motivo: actual.motivo,
      testigoId: actual.testigoId,
      foto: actual.foto,
      fechaMomento: actual.fechaMomento,
      lat: actual.lat,
      lng: actual.lng,
      precisionM: actual.precisionM,
    );
    try {
      await archivos.borrar(actual.foto.ruta);
    } catch (_) {
      // Una foto que no se pudo borrar no invalida una constancia ya registrada.
    }
  }

  /// La constancia va en dos pasos, como un hallazgo: primero el archivo, y el progreso se guarda, de modo
  /// que un corte entre la subida y el registro no vuelve a subir la foto.
  Future<void> _ejecutarNoFirma(NoFirmaJob job) async {
    var actual = job;
    if (actual.foto.storagePath == null) {
      final subido = await gateway.subirImagen(await archivos.leerBytes(actual.foto.ruta), nombre: 'no-firma.jpg');
      actual = actual.conFoto(actual.foto.copyWith(storagePath: subido.storagePath, hash: subido.hash));
      await cola.guardar(actual);
    }
    await gateway.registrarNoFirma(
      actual.inspeccionId,
      entregaId: actual.entregaId,
      recibeId: actual.recibeId,
      parte: actual.parte,
      motivo: actual.motivo,
      testigoId: actual.testigoId,
      foto: actual.foto,
    );
    try {
      await archivos.borrar(actual.foto.ruta);
    } catch (_) {
      // Un archivo que no se pudo borrar no invalida una constancia ya registrada.
    }
  }

  /// Un recibo con reservas no es una firma con un campo extra: va por su propio endpoint y cierra la
  /// transferencia de otra manera.
  Future<void> _ejecutarFirma(FirmaJob job) {
    if (job.reservas case final reservas?) {
      return gateway.recibirConReservas(
        job.inspeccionId,
        entregaId: job.entregaId,
        recibeId: job.recibeId,
        firma: job.firma,
        reservas: reservas,
      );
    }
    return gateway.firmarTransferencia(
      job.inspeccionId,
      entregaId: job.entregaId,
      recibeId: job.recibeId,
      firma: job.firma,
    );
  }

  /// La novedad del conductor: primero la foto (y se guarda su ruta, para no volver a subirla si se corta), después la
  /// novedad. Un 409 `ETAPA_PREVIA_SIN_CIERRE` no es un fallo: el Cargue todavía no está cerrado, la novedad queda en
  /// espera con el aviso «Falta cerrar …» y se reintenta en la próxima pasada.
  Future<_Desenlace> _ejecutarNovedad(NovedadJob inicial) async {
    var job = inicial;
    if (job.foto.storagePath == null) {
      final subido = await gateway.subirImagen(await archivos.leerBytes(job.foto.ruta), nombre: 'novedad.jpg');
      job = job.conFoto(job.foto.copyWith(storagePath: subido.storagePath, hash: subido.hash));
      await cola.guardar(job);
    }
    try {
      await gateway.crearNovedad(
        job.viajeId,
        job.vin,
        tipoNovedad: job.tipoNovedad,
        tamanoDano: job.tamanoDano,
        fecha: job.fecha,
        lat: job.lat,
        lng: job.lng,
        foto: job.foto,
        // Hueco 6 (S8-HU-07.3 CA-4): el id del job es el del envío. Si el primer envío llegó y la respuesta se perdió,
        // el reenvío recibe la misma novedad (200) y la cola la da por registrada, sin otra foto ni otro aviso.
        idEnvio: job.id,
      );
    } on ErrorApi catch (e) {
      if (await _enEspera(job, e)) return _Desenlace.espera;
      rethrow;
    }
    try {
      await archivos.borrar(job.foto.ruta);
    } catch (_) {
      // Un archivo que no se pudo borrar no invalida una novedad ya registrada.
    }
    return _Desenlace.hecho;
  }

  /// La novedad del viaje (S8-HU-07.3 CA-3): la foto, si la trae, se sube primero y queda anotada; después se
  /// registra con la hora y el GPS de cuando se guardó y con el id del job como `id_envio`, así un reenvío no crea
  /// otra novedad ni otro aviso. No espera a ninguna etapa: no cuelga de un carro.
  Future<void> _ejecutarNovedadViaje(NovedadViajeJob inicial) async {
    var job = inicial;
    if (job.foto case final foto? when foto.storagePath == null) {
      final subido = await gateway.subirImagen(await archivos.leerBytes(foto.ruta), nombre: 'novedad-viaje.jpg');
      job = job.conFoto(foto.copyWith(storagePath: subido.storagePath, hash: subido.hash));
      await cola.guardar(job);
    }
    await gateway.registrarNovedadViaje(
      job.viajeId,
      tipo: job.tipoNovedad,
      texto: job.texto,
      fecha: job.fecha,
      lat: job.lat,
      lng: job.lng,
      foto: job.foto,
      idEnvio: job.id,
      proceso: job.proceso,
      lugarId: job.lugarId,
    );
    await _borrarRutas([?job.foto?.ruta]);
  }

  /// «Llegué». Un 409 `LLEGADA_YA_CERRADA` es un éxito para un reintento: la primera confirmación ya quedó y el
  /// servidor no repite nada. Con `ETAPA_PREVIA_SIN_CIERRE` espera, igual que la novedad.
  Future<_Desenlace> _ejecutarLlegada(LlegadaJob job) async {
    try {
      await gateway.confirmarLlegada(job.viajeId, job.vin, fecha: job.fecha, lat: job.lat, lng: job.lng);
    } on ErrorApi catch (e) {
      if (e.status == 409 && e.code == codigoLlegadaYaCerrada) return _Desenlace.hecho;
      if (await _enEspera(job, e)) return _Desenlace.espera;
      rethrow;
    }
    return _Desenlace.hecho;
  }

  /// Si el error es que falta cerrar la etapa anterior: deja el job en la cola, sin gastar un intento ni marcarlo
  /// fallido, con el mensaje de lo que falta.
  ///
  /// Solo la usan la novedad y «Llegué», cuya etapa anterior es el cargue: cuando lo que falta es su entrega
  /// (`transferencia_sin_cierre`), se dice como lo dicen los botones grises (S8-HU-07.3 CA-5).
  Future<bool> _enEspera(Job job, ErrorApi e) async {
    if (e.status != 409 || e.code != codigoEtapaPreviaSinCierre) return false;
    final (etapa, motivo) = switch (e.details) {
      final Map m => (m['etapa_previa'] as String?, m['motivo'] as String?),
      _ => (null, null),
    };
    await cola.guardar(job.conIntento(
      intentos: job.intentos,
      error: switch (etapa) {
        _ when motivo == motivoTransferenciaSinCierre => mensajeEntregaDelCargueSinCerrar,
        null => 'Falta cerrar la etapa anterior',
        final etapa => 'Falta cerrar $etapa',
      },
      codigo: e.code,
      fallido: false,
    ));
    return true;
  }

  /// Un 409 «No hay unidades pendientes» es que la entrega ya quedó cerrada: lo más probable es que el envío anterior
  /// sí llegó y solo se perdió la respuesta. Se toma como hecho, así reintentar no deja nada a medias ni firma dos veces.
  ///
  /// Las fotos del cierre con una sola firma se suben primero, una a una, y su ruta queda guardada: un corte a mitad
  /// no vuelve a subir las que ya llegaron (S8-HU-04.2 CA-7).
  Future<void> _ejecutarEntregaPunto(EntregaPuntoJob inicial) async {
    var job = inicial;
    for (var i = 0; i < job.fotos.length; i++) {
      final foto = job.fotos[i];
      if (foto.storagePath != null) continue;
      final subido = await gateway.subirImagen(await archivos.leerBytes(foto.ruta), nombre: 'cierre-parada-$i.jpg');
      job = job.conFotos(_reemplazar(job.fotos, i, foto.copyWith(storagePath: subido.storagePath, hash: subido.hash)));
      await cola.guardar(job);
    }
    try {
      if (job.motivoCodigo case final motivo?) {
        await gateway.cerrarEntregaConUnaFirma(
          job.viajeId,
          job.lugarId,
          motivoCodigo: motivo,
          motivoTexto: job.motivoTexto,
          firma: job.firma,
          // Cada foto va a cada carro de la parada: el servidor la guarda en la revisión de llegada del carro que se
          // nombra, y así ninguno queda sin la constancia de su cierre.
          fotos: [
            for (final foto in job.fotos)
              for (final vin in job.vines) (vin: vin, foto: foto),
          ],
          testigoNombre: job.testigoNombre,
          testigoCedula: job.testigoCedula,
          fechaMomento: job.fechaMomento,
          lat: job.lat,
          lng: job.lng,
          precisionM: job.precisionM,
        );
      } else {
        await gateway.firmarEntregaPunto(
          job.viajeId,
          job.lugarId,
          firma: job.firma!,
          recibeNombre: job.recibeNombre,
          recibeCedula: job.recibeCedula,
          firmaRecibe: job.firmaRecibe,
          reservas: job.reservas,
          rechazos: job.rechazos,
          fechaMomento: job.fechaMomento,
          lat: job.lat,
          lng: job.lng,
          precisionM: job.precisionM,
        );
      }
    } on ErrorApi catch (e) {
      if (e.status != 409 || !e.message.startsWith(mensajeSinPendientes)) rethrow;
    }
    await _borrarRutas([for (final f in job.fotos) f.ruta]);
  }

  Future<void> _borrarRutas(List<String> rutas) async {
    for (final ruta in rutas) {
      try {
        await archivos.borrar(ruta);
      } catch (_) {
        // Un archivo que no se pudo borrar no invalida una entrega ya registrada.
      }
    }
  }

  Future<void> _borrarFotos(InformeJob job) async {
    // La foto de cada daño también: sin ella, cada informe con un daño dejaba un archivo sin dueño en el teléfono
    // (S8-HU-11.2 CA-5).
    for (final foto in [
      ...job.fotos,
      for (final h in job.hallazgos) ?h.foto,
      for (final e in job.extras) ...e.fotos,
    ]) {
      try {
        await archivos.borrar(foto.ruta);
      } catch (_) {
        // Un archivo que no se pudo borrar no debe invalidar un envío exitoso.
      }
    }
  }

  /// Copia la lista con un elemento cambiado. Sirve para las fotos y para los hallazgos: en los dos casos lo que
  /// se guarda es el progreso de un elemento, no la lista entera.
  List<T> _reemplazar<T>(List<T> lista, int indice, T nuevo) =>
      [for (var i = 0; i < lista.length; i++) i == indice ? nuevo : lista[i]];
}
