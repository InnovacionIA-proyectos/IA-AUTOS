import 'dart:io';

import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/features/inspecciones/domain/extra_local.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_local.dart';
import 'package:traza_autos/features/sync/data/cola_drift.dart';
import 'package:traza_autos/features/sync/domain/job.dart';

import '../support/dobles_sync.dart';

void main() {
  late AppDatabase db;
  late ColaDrift cola;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cola = ColaDrift(db);
  });
  tearDown(() => db.close());

  test('un job encolado por una versión anterior se sigue leyendo y enviando', () async {
    // Antes de S5-HU-22.9 el informe llevaba conductor_nombre, placa y trailer. Al actualizar la app con informes
    // sin enviar, esas claves sobran: se ignoran. Lo que NO puede pasar es que el job deje de poder leerse, porque
    // `listar()` descarta en silencio las filas ilegibles y el informe desaparecería de la cola sin remedio.
    const viejo = '{"kind":"informe","id":"a","etiqueta":"Informe VIN123 · Cargue","creado_en":1,"intentos":0,'
        '"fallido":false,"etapa_id":"etapa-1","momento":"informe","fecha":"2026-01-02T10:00:00.000Z",'
        '"lat":6.2,"lng":-75.5,"conductor_nombre":"Ana","placa":"ABC123","trailer":"T-9","notas":"ojo",'
        '"fotos":[],"hallazgos":[],"inspeccion_id":null,"hallazgos_enviados":0,"cerrada":false}';
    await db.into(db.jobs).insert(JobsCompanion.insert(id: 'a', payload: viejo));

    final jobs = await cola.listar();

    expect(jobs, hasLength(1), reason: 'una fila ilegible se descartaría en silencio');
    final job = jobs.single as InformeJob;
    expect((job.id, job.etapaId, job.notas), ('a', 'etapa-1', 'ojo'));
    expect(job.fotos, isEmpty);
  });

  test('conserva el orden de inserción (FIFO) aunque se modifiquen jobs antiguos', () async {
    await cola.agregar(informe(id: 'a'));
    await cola.agregar(informe(id: 'b'));
    await cola.agregar(informe(id: 'c'));

    await cola.guardar((await cola.listar()).first.conIntento(intentos: 3, error: 'x', fallido: false));

    expect((await cola.listar()).map((j) => j.id), ['a', 'b', 'c']);
  });

  test('persiste todo el progreso de un informe y lo recupera igual', () async {
    final original = informe(
      fotos: [foto('a.jpg', storagePath: 'ruta/1.jpg', enviada: true), foto('b.jpg')],
      hallazgos: [hallazgoEjemplo],
      inspeccionId: 'insp-7',
      hallazgosEnviados: 1,
      intentos: 2,
    );
    await cola.agregar(original);

    final leido = (await cola.listar()).single as InformeJob;

    expect(leido.toJson(), original.toJson());
    expect(leido.fotos[0].enviada, isTrue);
    expect(leido.fotos[1].selloLat, 6.21);
    expect((leido.hallazgos.single.cuadranteNumero, leido.hallazgos.single.lado), (26, 'derecho'));
    expect(leido.hallazgos.single.nota, hallazgoEjemplo.nota);
  });

  // Lo que encoló la versión anterior (parte y coords, sin cuadrante) se sigue leyendo: si fallara al leerse, la
  // cola lo saltaría sin mostrarlo. El servidor lo rechazará con 400 y quedará en la Cola para descartarlo.
  test('los extras se guardan con el informe, y un informe sin extras se lee igual', () {
    final job = InformeJob(
      id: 'j-extras',
      etiqueta: 'Informe',
      creadoEn: 1,
      etapaId: 'e1',
      momento: 'salida',
      fecha: '2026-01-02T10:00:00.000Z',
      lat: 6.2,
      lng: -75.5,
      extras: const [
        ExtraLocal(
          tipo: 'otro',
          nombre: 'Otro: portabicicletas',
          textoOtro: 'portabicicletas',
          observacion: 'rayón en el soporte',
          fotos: [FotoLocal(ruta: 'extra.jpg', selloFecha: '2026-01-02T10:00:00.000Z')],
        ),
      ],
    );

    final leido = Job.fromJson(job.toJson()) as InformeJob;
    final extra = leido.extras.single;
    expect((extra.tipo, extra.nombre, extra.textoOtro, extra.observacion), ('otro', 'Otro: portabicicletas', 'portabicicletas', 'rayón en el soporte'));
    expect(extra.fotos.single.ruta, 'extra.jpg');

    final sinExtras = job.toJson()..remove('extras');
    expect((Job.fromJson(sinExtras) as InformeJob).extras, isEmpty);
  });

  test('la cola conserva que el daño ya estaba registrado y cuál era', () {
    const hallazgo = HallazgoLocal(
      cuadranteNumero: 7,
      lado: 'izquierdo',
      zona: 'Izquierdo 7 · Espejo retrovisor',
      clase: 'golpe',
      tamano: '1_5cm',
      nivel: 'v2',
      esPreexistente: true,
      hallazgoOrigenId: 'h-previo',
    );

    final leido = HallazgoLocal.fromJson(hallazgo.toJson());

    expect((leido.esPreexistente, leido.hallazgoOrigenId), (true, 'h-previo'));
  });

  test('un hallazgo encolado por la versión anterior se lee, sin cuadrante', () {
    final viejo = HallazgoLocal.fromJson({
      'parte': 'Capot',
      'clase': 'golpe',
      'tamano': '1_5cm',
      'coords': {'x': 0.5, 'y': 0.66},
      'foto': null,
    });
    expect((viejo.cuadranteNumero, viejo.zona, viejo.clave), (null, 'Capot', null));
  });

  test('persiste una firma', () async {
    const firma = FirmaJob(
      id: 'f',
      etiqueta: 'Firma',
      creadoEn: 5,
      inspeccionId: 'i',
      entregaId: 'u1',
      recibeId: 'u2',
      firma: 'M 0 0 L 1 1',
    );
    await cola.agregar(firma);

    expect(((await cola.listar()).single as FirmaJob).toJson(), firma.toJson());
  });

  // Una firma encolada antes de que la API pidiera un solo campo: se lee y se manda la que haya.
  test('una firma del formato viejo se sigue leyendo', () {
    final vieja = Job.fromJson({
      'kind': 'firma',
      'id': 'f',
      'etiqueta': 'Firma',
      'creado_en': 5,
      'inspeccion_id': 'i',
      'entrega_id': 'u1',
      'recibe_id': 'u2',
      'firma_entrega': 'M 0 0 L 1 1',
    }) as FirmaJob;

    expect(vieja.firma, 'M 0 0 L 1 1');
  });

  test('eliminar quita solo el job indicado y guardar de un id inexistente no crea nada', () async {
    await cola.agregar(informe(id: 'a'));
    await cola.agregar(informe(id: 'b'));

    await cola.eliminar('a');
    await cola.guardar(informe(id: 'fantasma'));

    expect((await cola.listar()).map((j) => j.id), ['b']);
  });

  test('una fila ilegible se ignora sin bloquear al resto', () async {
    await cola.agregar(informe(id: 'a'));
    await db.customStatement("INSERT INTO jobs (id, payload) VALUES ('roto', '{no es json')");
    await cola.agregar(informe(id: 'b'));

    expect((await cola.listar()).map((j) => j.id), ['a', 'b']);
  });

  test('no admite dos jobs con el mismo id', () async {
    await cola.agregar(informe(id: 'a'));
    expect(() => cola.agregar(informe(id: 'a')), throwsA(anything));
  });

  test('los datos sobreviven a cerrar y reabrir la base (persistencia real en archivo)', () async {
    final carpeta = Directory.systemTemp.createTempSync('cola_drift_');
    addTearDown(() => carpeta.deleteSync(recursive: true));
    final archivo = File('${carpeta.path}/cola.sqlite');

    final primera = AppDatabase(NativeDatabase(archivo));
    await ColaDrift(primera).agregar(informe(id: 'persistente', fotos: [foto('a.jpg')], intentos: 3));
    await primera.close();

    final segunda = AppDatabase(NativeDatabase(archivo));
    addTearDown(segunda.close);
    final jobs = await ColaDrift(segunda).listar();

    expect(jobs.single.id, 'persistente');
    expect(jobs.single.intentos, 3);
    expect((jobs.single as InformeJob).fotos.single.ruta, 'a.jpg');
  });

  group('caché de lecturas', () {
    test('escribe, lee y reemplaza por clave', () async {
      final cache = CacheLecturasRepo(db);
      expect(await cache.leer('k'), isNull);

      await cache.escribir('k', 'uno');
      await cache.escribir('k', 'dos');

      expect(await cache.leer('k'), 'dos');
    });

    test('borrarTodo la vacía', () async {
      final cache = CacheLecturasRepo(db);
      await cache.escribir('k', 'v');
      await cache.borrarTodo();
      expect(await cache.leer('k'), isNull);
    });
  });
}
