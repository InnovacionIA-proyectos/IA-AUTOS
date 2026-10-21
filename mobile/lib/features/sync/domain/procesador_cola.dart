import '../../../core/errores/error_api.dart';
import '../../inspecciones/domain/foto_local.dart';
import 'job.dart';
import 'puertos.dart';

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

  /// Cantidad de jobs que aún se reintentarán solos.
  Future<int> contarPendientes() async => (await cola.listar()).where((j) => !j.fallido).length;

  Future<int> contarFallidos() async => (await cola.listar()).where((j) => j.fallido).length;

  Future<ResultadoSync> _resultado(int enviados) async {
    final jobs = await cola.listar();
    return ResultadoSync(
      enviados: enviados,
      pendientes: jobs.where((j) => !j.fallido).length,
      fallidos: jobs.where((j) => j.fallido).length,
    );
  }

  Future<ResultadoSync> procesar() async {
    if (_procesando) return _resultado(0);
    _procesando = true;
    var enviados = 0;
    try {
      // No empezar a drenar si la señal no es estable o la API no responde.
      if (!await red.redEstable() || !await red.apiAlcanzable()) return _resultado(0);

      while (true) {
        if (!await red.redEstable()) break;
        final job = (await cola.listar()).where((j) => !j.fallido).firstOrNull;
        if (job == null) break;

        try {
          switch (job) {
            case InformeJob():
              await _ejecutarInforme(job);
            case FirmaJob():
              await _ejecutarFirma(job);
          }
          await cola.eliminar(job.id);
          enviados++;
        } catch (e) {
          final actual = (await cola.listar()).where((j) => j.id == job.id).firstOrNull;
          if (actual == null) continue;
          final transitorio = e is! ErrorApi || e.esTransitorio;
          final intentos = actual.intentos + 1;
          await cola.guardar(
            actual.conIntento(
              intentos: intentos,
              error: e is ErrorApi ? e.message : e.toString(),
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
    return _resultado(enviados);
  }

  /// Devuelve un job fallido a la cola para que se reintente (acción manual del usuario).
  Future<void> reintentar(String id) async {
    final job = (await cola.listar()).where((j) => j.id == id).firstOrNull;
    if (job == null) return;
    await cola.guardar(job.conIntento(intentos: 0, error: null, fallido: false));
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
      );
      job = job.conProgreso(inspeccionId: id);
      await cola.guardar(job);
    }
    final inspeccionId = job.inspeccionId!;

    // 2) Fotos: subir (si falta) y crear la evidencia con el sello PROPIO de cada foto (si falta).
    for (var i = 0; i < job.fotos.length; i++) {
      var foto = job.fotos[i];
      if (foto.storagePath == null) {
        final storagePath = await gateway.subirImagen(await archivos.leerBase64(foto.ruta));
        foto = foto.copyWith(storagePath: storagePath);
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
        );
        foto = foto.copyWith(enviada: true);
        job = job.conProgreso(fotos: _reemplazar(job.fotos, i, foto));
        await cola.guardar(job);
      }
    }

    // 3) Hallazgos (reanudable por índice).
    for (var i = job.hallazgosEnviados; i < job.hallazgos.length; i++) {
      await gateway.agregarHallazgo(inspeccionId, job.hallazgos[i]);
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

  Future<void> _ejecutarFirma(FirmaJob job) => gateway.firmarTransferencia(
        job.inspeccionId,
        entregaId: job.entregaId,
        recibeId: job.recibeId,
        firmaEntrega: job.firmaEntrega,
        firmaRecibe: job.firmaRecibe,
      );

  Future<void> _borrarFotos(InformeJob job) async {
    for (final foto in job.fotos) {
      try {
        await archivos.borrar(foto.ruta);
      } catch (_) {
        // Un archivo que no se pudo borrar no debe invalidar un envío exitoso.
      }
    }
  }

  List<FotoLocal> _reemplazar(List<FotoLocal> fotos, int indice, FotoLocal nueva) =>
      [for (var i = 0; i < fotos.length; i++) i == indice ? nueva : fotos[i]];
}
