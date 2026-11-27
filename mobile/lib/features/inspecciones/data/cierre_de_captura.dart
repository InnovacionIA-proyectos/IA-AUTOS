import '../../../core/db/app_database.dart';
import '../../../core/db/cache_lecturas.dart';
import '../../sync/data/cola_drift.dart';
import '../../sync/domain/job.dart';

/// Encolar el informe y soltar su borrador son un solo paso (S8-HU-11.2 CA-4 y CA-5).
///
/// Hechos por separado, un corte entre los dos dejaba el borrador junto a su job: al volver se ofrecía otra vez, y
/// «Empezar de nuevo» borraba fotos que el job todavía tenía que subir, o «Continuar» encolaba el mismo informe
/// dos veces.
abstract interface class CierreDeCaptura {
  Future<void> encolar(Job job, {String? claveBorrador});
}

/// La cola y los borradores viven en la misma base del teléfono: una transacción hace las dos cosas o ninguna.
class CierreDeCapturaDrift implements CierreDeCaptura {
  CierreDeCapturaDrift(this._db);

  final AppDatabase _db;

  @override
  Future<void> encolar(Job job, {String? claveBorrador}) => _db.transaction(() async {
        await ColaDrift(_db).agregar(job);
        if (claveBorrador != null) await CacheLecturasRepo(_db).borrar(claveBorrador);
      });
}
