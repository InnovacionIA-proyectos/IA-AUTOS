import 'dart:io';

import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/features/sync/data/cola_drift.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/vehiculos/domain/coords_diagrama.dart';

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
    expect(leido.hallazgos.single.coords, const CoordsDiagrama(0.5, 0.66));
  });

  test('persiste una firma', () async {
    const firma = FirmaJob(id: 'f', etiqueta: 'Firma', creadoEn: 5, inspeccionId: 'i', entregaId: 'u1', recibeId: 'u2', firmaRecibe: 'M 0 0 L 1 1');
    await cola.agregar(firma);

    expect(((await cola.listar()).single as FirmaJob).toJson(), firma.toJson());
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
