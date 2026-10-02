import 'package:drift/drift.dart';
import 'package:drift_flutter/drift_flutter.dart';

part 'app_database.g.dart';

/// Cola de sincronización. `seq` autoincremental garantiza el orden FIFO; el resto del job vive en `payload`
/// (JSON) para poder evolucionar su forma sin migrar columnas.
@DataClassName('FilaJob')
class Jobs extends Table {
  IntColumn get seq => integer().autoIncrement()();
  TextColumn get id => text().unique()();
  TextColumn get payload => text()();
}

/// Caché de lecturas para poder mostrar la última lista sin señal.
@DataClassName('FilaCache')
class CacheLecturas extends Table {
  TextColumn get clave => text()();
  TextColumn get valor => text()();
  IntColumn get actualizadoEn => integer()();

  @override
  Set<Column> get primaryKey => {clave};
}

@DriftDatabase(tables: [Jobs, CacheLecturas])
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.e);

  /// Base de datos en el almacenamiento privado de la app.
  AppDatabase.abrir() : super(driftDatabase(name: 'traza_autos'));

  @override
  int get schemaVersion => 1;
}
