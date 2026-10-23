import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_local.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';

import '../support/dobles_sync.dart';

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late RedFalsa red;
  late ArchivosFalsos archivos;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    red = RedFalsa();
    archivos = ArchivosFalsos(['a.jpg', 'b.jpg']);
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: red, archivos: archivos);
  });

  group('envío de un informe', () {
    test('ejecuta los pasos en orden, usa el sello propio de cada foto, limpia archivos y saca el job de la cola', () async {
      await cola.agregar(informe(
        fotos: [foto('a.jpg', fecha: '2026-01-02T10:01:00.000Z', lat: 6.21), foto('b.jpg', fecha: '2026-01-02T10:05:00.000Z', lat: 6.25)],
        hallazgos: [hallazgoEjemplo, hallazgoEjemplo],
      ));

      final r = await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, [
        'crearInspeccion',
        'subirImagen',
        'agregarEvidencia',
        'subirImagen',
        'agregarEvidencia',
        'agregarHallazgo',
        'agregarHallazgo',
        'cerrarInspeccion',
      ]);
      expect(gateway.evidencias.map((e) => e['fecha']), ['2026-01-02T10:01:00.000Z', '2026-01-02T10:05:00.000Z']);
      expect(gateway.evidencias.map((e) => e['lat']), [6.21, 6.25]);
      expect(archivos.borrados, ['a.jpg', 'b.jpg']);
      expect(cola.jobs, isEmpty);
      expect((r.enviados, r.pendientes, r.fallidos), (1, 0, 0));
    });

    test('un informe sin novedades ni fotos solo crea y cierra', () async {
      await cola.agregar(informe());
      await procesador.procesar(usuarioPrueba);
      expect(gateway.llamadas, ['crearInspeccion', 'cerrarInspeccion']);
    });

    test('es reanudable: no repite la inspección, las fotos ya subidas ni los hallazgos ya enviados', () async {
      await cola.agregar(informe(
        inspeccionId: 'insp-previa',
        fotos: [foto('a.jpg', storagePath: 'ruta/x.jpg', enviada: true), foto('b.jpg', storagePath: 'ruta/y.jpg')],
        hallazgos: [hallazgoEjemplo, hallazgoEjemplo, hallazgoEjemplo],
        hallazgosEnviados: 2,
      ));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, ['agregarEvidencia', 'agregarHallazgo', 'cerrarInspeccion']);
      expect(gateway.evidencias.single['path'], 'ruta/y.jpg');
    });

    test('si se corta a mitad, guarda el progreso y al reintentar continúa donde quedó', () async {
      await cola.agregar(informe(fotos: [foto('a.jpg'), foto('b.jpg')], hallazgos: [hallazgoEjemplo]));
      gateway.fallarEnLlamada('subirImagen', 2, errorHttp(503)); // la 1ª foto sube bien, la 2ª falla

      final primera = await procesador.procesar(usuarioPrueba);
      expect(primera.enviados, 0);
      expect(primera.pendientes, 1);
      final guardado = cola.jobs.single as InformeJob;
      expect(guardado.inspeccionId, 'insp-1');
      expect(guardado.fotos[0].enviada, isTrue);
      expect(guardado.fotos[1].storagePath, isNull);
      expect(guardado.intentos, 1);

      gateway.llamadas.clear();
      final segunda = await procesador.procesar(usuarioPrueba);

      expect(segunda.enviados, 1);
      expect(gateway.llamadas, ['subirImagen', 'agregarEvidencia', 'agregarHallazgo', 'cerrarInspeccion']);
      expect(cola.jobs, isEmpty);
    });

    test('409 al cerrar significa "ya estaba cerrada" y cuenta como éxito', () async {
      await cola.agregar(informe());
      gateway.errores['cerrarInspeccion'] = [errorHttp(409)];

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.enviados, 1);
      expect(cola.jobs, isEmpty);
    });

    test('un 409 en otro paso NO se trata como éxito: es un error no recuperable', () async {
      await cola.agregar(informe(hallazgos: [hallazgoEjemplo]));
      gateway.errores['agregarHallazgo'] = [errorHttp(409, 'Inspección cerrada')];

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.enviados, 0);
      expect((cola.jobs.single).fallido, isTrue);
    });

    test('un fallo al borrar un archivo local no invalida un envío exitoso', () async {
      await cola.agregar(informe(fotos: [foto('no-existe.jpg')]));
      archivos.existentes.add('no-existe.jpg');
      final conFallo = _ArchivosQueFallanAlBorrar(archivos);
      procesador = ProcesadorCola(cola: cola, gateway: gateway, red: red, archivos: conFallo);

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.enviados, 1);
      expect(cola.jobs, isEmpty);
    });
  });

  group('envío de una firma', () {
    test('envía la transferencia con las firmas que haya', () async {
      await cola.agregar(const FirmaJob(
        id: 'f1',
        etiqueta: 'Firma',
        creadoEn: 1,
        usuarioId: usuarioPrueba,
        inspeccionId: 'insp-9',
        entregaId: 'u1',
        recibeId: 'u2',
        firmaEntrega: 'M 1 1 L 2 2',
      ));

      final r = await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, ['firmarTransferencia']);
      expect(r.enviados, 1);
      expect(cola.jobs, isEmpty);
    });
  });

  group('orden y señal', () {
    test('es FIFO y un job a la vez', () async {
      await cola.agregar(informe(id: 'primero'));
      await cola.agregar(informe(id: 'segundo'));

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.enviados, 2);
      expect(gateway.llamadas, ['crearInspeccion', 'cerrarInspeccion', 'crearInspeccion', 'cerrarInspeccion']);
    });

    test('un error transitorio detiene la pasada y NO intenta el siguiente (no rompe el orden)', () async {
      await cola.agregar(informe(id: 'primero'));
      await cola.agregar(informe(id: 'segundo'));
      gateway.errores['crearInspeccion'] = [errorHttp(503)];

      final r = await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, ['crearInspeccion']);
      expect(r.enviados, 0);
      expect((r.pendientes, r.fallidos), (2, 0));
      expect(cola.jobs.first.intentos, 1);
      expect(cola.jobs.last.intentos, 0);
    });

    test('sin señal estable no hace nada', () async {
      await cola.agregar(informe());
      red.estable = false;

      final r = await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, isEmpty);
      expect(r.pendientes, 1);
    });

    test('con red pero sin que la API responda no hace nada', () async {
      await cola.agregar(informe());
      red.alcanzable = false;

      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, isEmpty);
    });

    test('re-valida la señal antes de cada job: si se cae entre uno y otro, se detiene', () async {
      await cola.agregar(informe(id: 'primero'));
      await cola.agregar(informe(id: 'segundo'));
      // 1ª consulta (listo para sincronizar) y 2ª (antes del primer job) estables; luego se cae.
      red.consultasEstables = 2;

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.enviados, 1);
      expect(cola.jobs.single.id, 'segundo');
    });

    test('no se reentra: una segunda llamada mientras procesa no duplica envíos', () async {
      await cola.agregar(informe());

      final resultados = await Future.wait([procesador.procesar(usuarioPrueba), procesador.procesar(usuarioPrueba)]);

      expect(gateway.llamadas.where((l) => l == 'crearInspeccion'), hasLength(1));
      expect(resultados.map((r) => r.enviados).reduce((a, b) => a + b), 1);
    });
  });

  group('clasificación de errores', () {
    for (final status in [0, 401, 408, 429, 500, 502, 503]) {
      test('$status es transitorio: el job se conserva con un intento más y la pasada se detiene', () async {
        await cola.agregar(informe(id: 'a'));
        await cola.agregar(informe(id: 'b'));
        gateway.errores['crearInspeccion'] = [status == 0 ? const ErrorApi.red() : errorHttp(status)];

        await procesador.procesar(usuarioPrueba);

        expect(cola.jobs.first.fallido, isFalse);
        expect(cola.jobs.first.intentos, 1);
        expect(gateway.llamadas, ['crearInspeccion']);
      });
    }

    for (final status in [400, 403, 404, 409, 422]) {
      test('$status no es recuperable: el job queda FALLIDO y se sigue con el siguiente', () async {
        await cola.agregar(informe(id: 'a'));
        await cola.agregar(informe(id: 'b'));
        gateway.errores['crearInspeccion'] = [errorHttp(status, 'Datos inválidos')];

        final r = await procesador.procesar(usuarioPrueba);

        final a = cola.jobs.firstWhere((j) => j.id == 'a');
        expect(a.fallido, isTrue);
        expect(a.error, 'Datos inválidos');
        expect(cola.jobs.any((j) => j.id == 'b'), isFalse); // el segundo sí se envió
        expect((r.enviados, r.fallidos), (1, 1));
      });
    }

    test('un error que no es de la API (por ejemplo un archivo ilegible) se trata como transitorio', () async {
      await cola.agregar(informe(fotos: [foto('falta.jpg')]));

      await procesador.procesar(usuarioPrueba);

      final job = cola.jobs.single;
      expect(job.fallido, isFalse);
      expect(job.intentos, 1);
    });

    test('al octavo intento fallido el job pasa a FALLIDO y deja de bloquear', () async {
      await cola.agregar(informe(id: 'a', intentos: 7));
      await cola.agregar(informe(id: 'b'));
      gateway.errores['crearInspeccion'] = [errorHttp(503)];

      final r = await procesador.procesar(usuarioPrueba);

      final a = cola.jobs.firstWhere((j) => j.id == 'a');
      expect((a.fallido, a.intentos), (true, 8));
      expect(r.enviados, 1); // 'b' se envió
    });

    test('hasta el séptimo intento sigue siendo reintentable', () async {
      await cola.agregar(informe(intentos: 6));
      gateway.errores['crearInspeccion'] = [errorHttp(503)];

      await procesador.procesar(usuarioPrueba);

      expect((cola.jobs.single.fallido, cola.jobs.single.intentos), (false, 7));
    });
  });

  group('trabajos fallidos (mejora sobre la app original: visibles, con reintento manual)', () {
    test('un job fallido no se reintenta solo ni cuenta como pendiente', () async {
      await cola.agregar(informe(fallido: true, intentos: 8));

      final r = await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, isEmpty);
      expect((r.pendientes, r.fallidos), (0, 1));
    });

    test('reintentar lo devuelve a la cola con intentos en cero y se envía', () async {
      await cola.agregar(informe(fallido: true, intentos: 8));

      await procesador.reintentar('j1', usuarioPrueba);
      expect((cola.jobs.single.fallido, cola.jobs.single.intentos, cola.jobs.single.error), (false, 0, null));

      final r = await procesador.procesar(usuarioPrueba);
      expect(r.enviados, 1);
    });

    test('descartar lo quita de la cola y borra sus fotos locales', () async {
      await cola.agregar(informe(fallido: true, fotos: [foto('a.jpg'), foto('b.jpg')]));

      await procesador.descartar('j1');

      expect(cola.jobs, isEmpty);
      expect(archivos.borrados, ['a.jpg', 'b.jpg']);
    });

    test('reintentar o descartar un id inexistente no hace nada', () async {
      await procesador.reintentar('nada', usuarioPrueba);
      await procesador.descartar('nada');
      expect(cola.jobs, isEmpty);
    });
  });

  group('lo que viaja con cada evidencia', () {
    test('la vista, el sello, la precision, el equipo, la nota y el hash del servidor', () async {
      await cola.agregar(informe(
        fotos: [
          FotoLocal(
            ruta: 'a.jpg',
            selloFecha: '2026-01-02T10:01:00.000Z',
            selloLat: 6.21,
            selloLng: -75.51,
            vistaId: 'v-frontal',
            nota: 'rayon',
            precision: 12.5,
            dispositivoId: 'equipo-1',
          ),
        ],
      ));

      await procesador.procesar(usuarioPrueba);

      final evidencia = gateway.evidencias.single;
      expect(evidencia['vista_id'], 'v-frontal');
      expect(evidencia['precision'], 12.5);
      expect(evidencia['dispositivo_id'], 'equipo-1');
      expect(evidencia['nota'], 'rayon');
      // El hash lo calcula el servidor al subir el archivo y la app lo reenvia: asi se detecta un archivo danado.
      expect(evidencia['hash_sha256'], 'hash-1');
    });
  });


  group('el hallazgo viaja con su foto', () {
    test('primero se sube el archivo y luego se crea el hallazgo con su evidencia', () async {
      await cola.agregar(informe(
        hallazgos: [
          HallazgoLocal(
            parte: 'Capot',
            clase: 'golpe',
            tamano: '1_5cm',
            foto: FotoLocal(ruta: 'a.jpg', selloFecha: '2026-01-02T10:01:00.000Z', selloLat: 6.2, selloLng: -75.5),
          ),
        ],
      ));

      await procesador.procesar(usuarioPrueba);

      // El orden importa: sin storage_path el hallazgo no se puede crear.
      expect(gateway.llamadas, containsAllInOrder(['subirImagen', 'agregarHallazgo']));
      expect(cola.jobs, isEmpty);
    });

    test('al reanudar no se vuelve a subir la foto del hallazgo', () async {
      // El hallazgo ya tiene su archivo subido: una pasada anterior llego hasta ahi y se corto.
      await cola.agregar(informe(
        hallazgos: [
          HallazgoLocal(
            parte: 'Capot',
            clase: 'golpe',
            tamano: '1_5cm',
            foto: const FotoLocal(
              ruta: 'a.jpg',
              selloFecha: '2026-01-02T10:01:00.000Z',
              storagePath: 'ruta/ya-subida.jpg',
              hash: 'hash-viejo',
            ),
          ),
        ],
      ));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas.where((l) => l == 'subirImagen'), isEmpty);
    });
  });

}

class _ArchivosQueFallanAlBorrar extends ArchivosFalsos {
  _ArchivosQueFallanAlBorrar(ArchivosFalsos base) : super(base.existentes);

  @override
  Future<void> borrar(String ruta) async => throw StateError('no se puede borrar');
}
