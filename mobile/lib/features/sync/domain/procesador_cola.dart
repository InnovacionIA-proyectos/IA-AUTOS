import '../../../core/errores/error_api.dart';
import 'job.dart';
import 'puertos.dart';

/// Códigos con que el servidor explica un 409 del conductor en ruta.
const codigoEtapaPreviaSinCierre = 'ETAPA_PREVIA_SIN_CIERRE';
const codigoLlegadaYaCerrada = 'LLEGADA_YA_CERRADA';

enum _Desenlace { hecho, espera }

/// Lo que responde el servidor a la firma de un punto que ya no tiene unidades por entregar.
const mensajeSinPendientes = 'No hay unidades pendientes de entrega';

/// Resultado de una pasada de sincronización.
class ResultadoSync {
  const ResultadoSync({required this.enviados, required this.pendientes, required this.fallidos});

  final int enviados;

  /// Jobs que todavía se reintentarán solos.
  final int pendientes;

  /// Jobs que necesitan atención del usuario (reintento manual o descarte).
  final int fallidos;
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

  Future<int> contarFallidos(String usuarioId) async => (await visibles(usuarioId)).where((j) => j.fallido).length;

  /// Adopta los jobs sin dueño para [usuarioId]. Se llama al arrancar con una sesión guardada: esa es, casi con
  /// certeza, quien los encoló, porque es la última que se usó antes de actualizar la app. Devuelve cuántos.
  Future<int> adoptarHuerfanos(String usuarioId) async {
    final huerfanos = (await cola.listar()).where((j) => j.esHuerfano).toList();
    for (final job in huerfanos) {
      await cola.guardar(job.conUsuario(usuarioId));
    }
    return huerfanos.length;
  }

  Future<ResultadoSync> _resultado(int enviados, String usuarioId) async {
    final jobs = await visibles(usuarioId);
    return ResultadoSync(
      enviados: enviados,
      pendientes: jobs.where((j) => !j.fallido).length,
      fallidos: jobs.where((j) => j.fallido).length,
    );
  }

  Future<ResultadoSync> procesar(String usuarioId) async {
    if (_procesando) return _resultado(0, usuarioId);
    _procesando = true;
    var enviados = 0;
    try {
      // No empezar a drenar si la señal no es estable o la API no responde.
      if (!await red.redEstable() || !await red.apiAlcanzable()) return _resultado(0, usuarioId);

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
            case ReciboSinEntregaJob():
              await gateway.firmarReciboSinEntrega(job.inspeccionId, firma: job.firma);
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
          }
          await cola.eliminar(job.id);
          enviados++;
        } catch (e) {
          final actual = (await cola.listar()).where((j) => j.id == job.id).firstOrNull;
          if (actual == null) continue;
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
              codigo: e is ErrorApi ? e.code : null,
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
    await cola.eliminar(id);
  }

  Future<void> _ejecutarInforme(InformeJob inicial) async {
    var job = inicial;

    // 1) Inspección (reanudable).
    if (job.inspeccionId == null) {
      final id = await gateway.crearInspeccion(
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

    // 4) Cerrar (idempotente: 409 "ya cerrada" cuenta como éxito).
    if (!job.cerrada) {
      try {
        await gateway.cerrarInspeccion(inspeccionId);
      } on ErrorApi catch (e) {
        if (e.status != 409) rethrow;
      }
      job = job.conProgreso(cerrada: true);
      await cola.guardar(job);
    }

    // 5) Limpiar los archivos locales.
    await _borrarFotos(job);
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
  Future<bool> _enEspera(Job job, ErrorApi e) async {
    if (e.status != 409 || e.code != codigoEtapaPreviaSinCierre) return false;
    final etapa = switch (e.details) { final Map m => m['etapa_previa'] as String?, _ => null };
    await cola.guardar(job.conIntento(
      intentos: job.intentos,
      error: etapa == null ? 'Falta cerrar la etapa anterior' : 'Falta cerrar $etapa',
      codigo: e.code,
      fallido: false,
    ));
    return true;
  }

  /// Un 409 «No hay unidades pendientes» es que la entrega ya quedó cerrada: lo más probable es que el envío anterior
  /// sí llegó y solo se perdió la respuesta. Se toma como hecho, así reintentar no deja nada a medias ni firma dos veces.
  Future<void> _ejecutarEntregaPunto(EntregaPuntoJob job) async {
    try {
      if (job.motivo case final motivo?) {
        await gateway.cerrarEntregaConUnaFirma(job.viajeId, job.lugarId, motivo: motivo, firma: job.firma);
      } else {
        await gateway.firmarEntregaPunto(job.viajeId, job.lugarId, firma: job.firma!);
      }
    } on ErrorApi catch (e) {
      if (e.status != 409 || !e.message.startsWith(mensajeSinPendientes)) rethrow;
    }
  }

  Future<void> _borrarFotos(InformeJob job) async {
    for (final foto in job.fotos) {
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
