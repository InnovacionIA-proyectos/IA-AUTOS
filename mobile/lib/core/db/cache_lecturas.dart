import 'app_database.dart';

/// Caché clave-valor de lecturas (JSON como texto).
class CacheLecturasRepo {
  CacheLecturasRepo(this._db);

  final AppDatabase _db;

  Future<String?> leer(String clave) async {
    final fila = await (_db.select(_db.cacheLecturas)..where((t) => t.clave.equals(clave))).getSingleOrNull();
    return fila?.valor;
  }

  Future<void> escribir(String clave, String valor) => _db.into(_db.cacheLecturas).insertOnConflictUpdate(
        CacheLecturasCompanion.insert(clave: clave, valor: valor, actualizadoEn: DateTime.now().millisecondsSinceEpoch),
      );

  Future<void> borrarTodo() => _db.delete(_db.cacheLecturas).go();
}
