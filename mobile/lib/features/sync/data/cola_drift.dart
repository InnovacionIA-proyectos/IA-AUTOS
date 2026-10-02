import 'dart:convert';

import 'package:drift/drift.dart';

import '../../../core/db/app_database.dart';
import '../domain/job.dart';
import '../domain/puertos.dart';

/// Cola persistente en SQLite (Drift). Cada escritura es una sentencia atómica, así que un corte del proceso nunca
/// deja un job a medias en disco.
class ColaDrift implements ColaRepositorio {
  ColaDrift(this._db);

  final AppDatabase _db;

  @override
  Future<void> agregar(Job job) => _db.into(_db.jobs).insert(JobsCompanion.insert(id: job.id, payload: jsonEncode(job.toJson())));

  @override
  Future<List<Job>> listar() async {
    final filas = await (_db.select(_db.jobs)..orderBy([(t) => OrderingTerm.asc(t.seq)])).get();
    final jobs = <Job>[];
    for (final fila in filas) {
      try {
        jobs.add(Job.fromJson(jsonDecode(fila.payload) as Map<String, dynamic>));
      } catch (_) {
        // Una fila ilegible no debe bloquear al resto de la cola.
      }
    }
    return jobs;
  }

  @override
  Future<void> guardar(Job job) =>
      (_db.update(_db.jobs)..where((t) => t.id.equals(job.id))).write(JobsCompanion(payload: Value(jsonEncode(job.toJson()))));

  @override
  Future<void> eliminar(String id) => (_db.delete(_db.jobs)..where((t) => t.id.equals(id))).go();
}
