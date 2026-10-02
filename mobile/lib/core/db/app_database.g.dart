// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'app_database.dart';

// ignore_for_file: type=lint
class $JobsTable extends Jobs with TableInfo<$JobsTable, FilaJob> {
  @override
  final GeneratedDatabase attachedDatabase;
  final String? _alias;
  $JobsTable(this.attachedDatabase, [this._alias]);
  static const VerificationMeta _seqMeta = const VerificationMeta('seq');
  @override
  late final GeneratedColumn<int> seq = GeneratedColumn<int>(
    'seq',
    aliasedName,
    false,
    hasAutoIncrement: true,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
    defaultConstraints: GeneratedColumn.constraintIsAlways(
      'PRIMARY KEY AUTOINCREMENT',
    ),
  );
  static const VerificationMeta _idMeta = const VerificationMeta('id');
  @override
  late final GeneratedColumn<String> id = GeneratedColumn<String>(
    'id',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
    defaultConstraints: GeneratedColumn.constraintIsAlways('UNIQUE'),
  );
  static const VerificationMeta _payloadMeta = const VerificationMeta(
    'payload',
  );
  @override
  late final GeneratedColumn<String> payload = GeneratedColumn<String>(
    'payload',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  @override
  List<GeneratedColumn> get $columns => [seq, id, payload];
  @override
  String get aliasedName => _alias ?? actualTableName;
  @override
  String get actualTableName => $name;
  static const String $name = 'jobs';
  @override
  VerificationContext validateIntegrity(
    Insertable<FilaJob> instance, {
    bool isInserting = false,
  }) {
    final context = VerificationContext();
    final data = instance.toColumns(true);
    if (data.containsKey('seq')) {
      context.handle(
        _seqMeta,
        seq.isAcceptableOrUnknown(data['seq']!, _seqMeta),
      );
    }
    if (data.containsKey('id')) {
      context.handle(_idMeta, id.isAcceptableOrUnknown(data['id']!, _idMeta));
    } else if (isInserting) {
      context.missing(_idMeta);
    }
    if (data.containsKey('payload')) {
      context.handle(
        _payloadMeta,
        payload.isAcceptableOrUnknown(data['payload']!, _payloadMeta),
      );
    } else if (isInserting) {
      context.missing(_payloadMeta);
    }
    return context;
  }

  @override
  Set<GeneratedColumn> get $primaryKey => {seq};
  @override
  FilaJob map(Map<String, dynamic> data, {String? tablePrefix}) {
    final effectivePrefix = tablePrefix != null ? '$tablePrefix.' : '';
    return FilaJob(
      seq: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}seq'],
      )!,
      id: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}id'],
      )!,
      payload: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}payload'],
      )!,
    );
  }

  @override
  $JobsTable createAlias(String alias) {
    return $JobsTable(attachedDatabase, alias);
  }
}

class FilaJob extends DataClass implements Insertable<FilaJob> {
  final int seq;
  final String id;
  final String payload;
  const FilaJob({required this.seq, required this.id, required this.payload});
  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    map['seq'] = Variable<int>(seq);
    map['id'] = Variable<String>(id);
    map['payload'] = Variable<String>(payload);
    return map;
  }

  JobsCompanion toCompanion(bool nullToAbsent) {
    return JobsCompanion(
      seq: Value(seq),
      id: Value(id),
      payload: Value(payload),
    );
  }

  factory FilaJob.fromJson(
    Map<String, dynamic> json, {
    ValueSerializer? serializer,
  }) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return FilaJob(
      seq: serializer.fromJson<int>(json['seq']),
      id: serializer.fromJson<String>(json['id']),
      payload: serializer.fromJson<String>(json['payload']),
    );
  }
  @override
  Map<String, dynamic> toJson({ValueSerializer? serializer}) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return <String, dynamic>{
      'seq': serializer.toJson<int>(seq),
      'id': serializer.toJson<String>(id),
      'payload': serializer.toJson<String>(payload),
    };
  }

  FilaJob copyWith({int? seq, String? id, String? payload}) => FilaJob(
    seq: seq ?? this.seq,
    id: id ?? this.id,
    payload: payload ?? this.payload,
  );
  FilaJob copyWithCompanion(JobsCompanion data) {
    return FilaJob(
      seq: data.seq.present ? data.seq.value : this.seq,
      id: data.id.present ? data.id.value : this.id,
      payload: data.payload.present ? data.payload.value : this.payload,
    );
  }

  @override
  String toString() {
    return (StringBuffer('FilaJob(')
          ..write('seq: $seq, ')
          ..write('id: $id, ')
          ..write('payload: $payload')
          ..write(')'))
        .toString();
  }

  @override
  int get hashCode => Object.hash(seq, id, payload);
  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      (other is FilaJob &&
          other.seq == this.seq &&
          other.id == this.id &&
          other.payload == this.payload);
}

class JobsCompanion extends UpdateCompanion<FilaJob> {
  final Value<int> seq;
  final Value<String> id;
  final Value<String> payload;
  const JobsCompanion({
    this.seq = const Value.absent(),
    this.id = const Value.absent(),
    this.payload = const Value.absent(),
  });
  JobsCompanion.insert({
    this.seq = const Value.absent(),
    required String id,
    required String payload,
  }) : id = Value(id),
       payload = Value(payload);
  static Insertable<FilaJob> custom({
    Expression<int>? seq,
    Expression<String>? id,
    Expression<String>? payload,
  }) {
    return RawValuesInsertable({
      if (seq != null) 'seq': seq,
      if (id != null) 'id': id,
      if (payload != null) 'payload': payload,
    });
  }

  JobsCompanion copyWith({
    Value<int>? seq,
    Value<String>? id,
    Value<String>? payload,
  }) {
    return JobsCompanion(
      seq: seq ?? this.seq,
      id: id ?? this.id,
      payload: payload ?? this.payload,
    );
  }

  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    if (seq.present) {
      map['seq'] = Variable<int>(seq.value);
    }
    if (id.present) {
      map['id'] = Variable<String>(id.value);
    }
    if (payload.present) {
      map['payload'] = Variable<String>(payload.value);
    }
    return map;
  }

  @override
  String toString() {
    return (StringBuffer('JobsCompanion(')
          ..write('seq: $seq, ')
          ..write('id: $id, ')
          ..write('payload: $payload')
          ..write(')'))
        .toString();
  }
}

class $CacheLecturasTable extends CacheLecturas
    with TableInfo<$CacheLecturasTable, FilaCache> {
  @override
  final GeneratedDatabase attachedDatabase;
  final String? _alias;
  $CacheLecturasTable(this.attachedDatabase, [this._alias]);
  static const VerificationMeta _claveMeta = const VerificationMeta('clave');
  @override
  late final GeneratedColumn<String> clave = GeneratedColumn<String>(
    'clave',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _valorMeta = const VerificationMeta('valor');
  @override
  late final GeneratedColumn<String> valor = GeneratedColumn<String>(
    'valor',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _actualizadoEnMeta = const VerificationMeta(
    'actualizadoEn',
  );
  @override
  late final GeneratedColumn<int> actualizadoEn = GeneratedColumn<int>(
    'actualizado_en',
    aliasedName,
    false,
    type: DriftSqlType.int,
    requiredDuringInsert: true,
  );
  @override
  List<GeneratedColumn> get $columns => [clave, valor, actualizadoEn];
  @override
  String get aliasedName => _alias ?? actualTableName;
  @override
  String get actualTableName => $name;
  static const String $name = 'cache_lecturas';
  @override
  VerificationContext validateIntegrity(
    Insertable<FilaCache> instance, {
    bool isInserting = false,
  }) {
    final context = VerificationContext();
    final data = instance.toColumns(true);
    if (data.containsKey('clave')) {
      context.handle(
        _claveMeta,
        clave.isAcceptableOrUnknown(data['clave']!, _claveMeta),
      );
    } else if (isInserting) {
      context.missing(_claveMeta);
    }
    if (data.containsKey('valor')) {
      context.handle(
        _valorMeta,
        valor.isAcceptableOrUnknown(data['valor']!, _valorMeta),
      );
    } else if (isInserting) {
      context.missing(_valorMeta);
    }
    if (data.containsKey('actualizado_en')) {
      context.handle(
        _actualizadoEnMeta,
        actualizadoEn.isAcceptableOrUnknown(
          data['actualizado_en']!,
          _actualizadoEnMeta,
        ),
      );
    } else if (isInserting) {
      context.missing(_actualizadoEnMeta);
    }
    return context;
  }

  @override
  Set<GeneratedColumn> get $primaryKey => {clave};
  @override
  FilaCache map(Map<String, dynamic> data, {String? tablePrefix}) {
    final effectivePrefix = tablePrefix != null ? '$tablePrefix.' : '';
    return FilaCache(
      clave: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}clave'],
      )!,
      valor: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}valor'],
      )!,
      actualizadoEn: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}actualizado_en'],
      )!,
    );
  }

  @override
  $CacheLecturasTable createAlias(String alias) {
    return $CacheLecturasTable(attachedDatabase, alias);
  }
}

class FilaCache extends DataClass implements Insertable<FilaCache> {
  final String clave;
  final String valor;
  final int actualizadoEn;
  const FilaCache({
    required this.clave,
    required this.valor,
    required this.actualizadoEn,
  });
  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    map['clave'] = Variable<String>(clave);
    map['valor'] = Variable<String>(valor);
    map['actualizado_en'] = Variable<int>(actualizadoEn);
    return map;
  }

  CacheLecturasCompanion toCompanion(bool nullToAbsent) {
    return CacheLecturasCompanion(
      clave: Value(clave),
      valor: Value(valor),
      actualizadoEn: Value(actualizadoEn),
    );
  }

  factory FilaCache.fromJson(
    Map<String, dynamic> json, {
    ValueSerializer? serializer,
  }) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return FilaCache(
      clave: serializer.fromJson<String>(json['clave']),
      valor: serializer.fromJson<String>(json['valor']),
      actualizadoEn: serializer.fromJson<int>(json['actualizadoEn']),
    );
  }
  @override
  Map<String, dynamic> toJson({ValueSerializer? serializer}) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return <String, dynamic>{
      'clave': serializer.toJson<String>(clave),
      'valor': serializer.toJson<String>(valor),
      'actualizadoEn': serializer.toJson<int>(actualizadoEn),
    };
  }

  FilaCache copyWith({String? clave, String? valor, int? actualizadoEn}) =>
      FilaCache(
        clave: clave ?? this.clave,
        valor: valor ?? this.valor,
        actualizadoEn: actualizadoEn ?? this.actualizadoEn,
      );
  FilaCache copyWithCompanion(CacheLecturasCompanion data) {
    return FilaCache(
      clave: data.clave.present ? data.clave.value : this.clave,
      valor: data.valor.present ? data.valor.value : this.valor,
      actualizadoEn: data.actualizadoEn.present
          ? data.actualizadoEn.value
          : this.actualizadoEn,
    );
  }

  @override
  String toString() {
    return (StringBuffer('FilaCache(')
          ..write('clave: $clave, ')
          ..write('valor: $valor, ')
          ..write('actualizadoEn: $actualizadoEn')
          ..write(')'))
        .toString();
  }

  @override
  int get hashCode => Object.hash(clave, valor, actualizadoEn);
  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      (other is FilaCache &&
          other.clave == this.clave &&
          other.valor == this.valor &&
          other.actualizadoEn == this.actualizadoEn);
}

class CacheLecturasCompanion extends UpdateCompanion<FilaCache> {
  final Value<String> clave;
  final Value<String> valor;
  final Value<int> actualizadoEn;
  final Value<int> rowid;
  const CacheLecturasCompanion({
    this.clave = const Value.absent(),
    this.valor = const Value.absent(),
    this.actualizadoEn = const Value.absent(),
    this.rowid = const Value.absent(),
  });
  CacheLecturasCompanion.insert({
    required String clave,
    required String valor,
    required int actualizadoEn,
    this.rowid = const Value.absent(),
  }) : clave = Value(clave),
       valor = Value(valor),
       actualizadoEn = Value(actualizadoEn);
  static Insertable<FilaCache> custom({
    Expression<String>? clave,
    Expression<String>? valor,
    Expression<int>? actualizadoEn,
    Expression<int>? rowid,
  }) {
    return RawValuesInsertable({
      if (clave != null) 'clave': clave,
      if (valor != null) 'valor': valor,
      if (actualizadoEn != null) 'actualizado_en': actualizadoEn,
      if (rowid != null) 'rowid': rowid,
    });
  }

  CacheLecturasCompanion copyWith({
    Value<String>? clave,
    Value<String>? valor,
    Value<int>? actualizadoEn,
    Value<int>? rowid,
  }) {
    return CacheLecturasCompanion(
      clave: clave ?? this.clave,
      valor: valor ?? this.valor,
      actualizadoEn: actualizadoEn ?? this.actualizadoEn,
      rowid: rowid ?? this.rowid,
    );
  }

  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    if (clave.present) {
      map['clave'] = Variable<String>(clave.value);
    }
    if (valor.present) {
      map['valor'] = Variable<String>(valor.value);
    }
    if (actualizadoEn.present) {
      map['actualizado_en'] = Variable<int>(actualizadoEn.value);
    }
    if (rowid.present) {
      map['rowid'] = Variable<int>(rowid.value);
    }
    return map;
  }

  @override
  String toString() {
    return (StringBuffer('CacheLecturasCompanion(')
          ..write('clave: $clave, ')
          ..write('valor: $valor, ')
          ..write('actualizadoEn: $actualizadoEn, ')
          ..write('rowid: $rowid')
          ..write(')'))
        .toString();
  }
}

abstract class _$AppDatabase extends GeneratedDatabase {
  _$AppDatabase(QueryExecutor e) : super(e);
  $AppDatabaseManager get managers => $AppDatabaseManager(this);
  late final $JobsTable jobs = $JobsTable(this);
  late final $CacheLecturasTable cacheLecturas = $CacheLecturasTable(this);
  @override
  Iterable<TableInfo<Table, Object?>> get allTables =>
      allSchemaEntities.whereType<TableInfo<Table, Object?>>();
  @override
  List<DatabaseSchemaEntity> get allSchemaEntities => [jobs, cacheLecturas];
}

typedef $$JobsTableCreateCompanionBuilder =
    JobsCompanion Function({
      Value<int> seq,
      required String id,
      required String payload,
    });
typedef $$JobsTableUpdateCompanionBuilder =
    JobsCompanion Function({
      Value<int> seq,
      Value<String> id,
      Value<String> payload,
    });

class $$JobsTableFilterComposer extends Composer<_$AppDatabase, $JobsTable> {
  $$JobsTableFilterComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnFilters<int> get seq => $composableBuilder(
    column: $table.seq,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get payload => $composableBuilder(
    column: $table.payload,
    builder: (column) => ColumnFilters(column),
  );
}

class $$JobsTableOrderingComposer extends Composer<_$AppDatabase, $JobsTable> {
  $$JobsTableOrderingComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnOrderings<int> get seq => $composableBuilder(
    column: $table.seq,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get payload => $composableBuilder(
    column: $table.payload,
    builder: (column) => ColumnOrderings(column),
  );
}

class $$JobsTableAnnotationComposer
    extends Composer<_$AppDatabase, $JobsTable> {
  $$JobsTableAnnotationComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  GeneratedColumn<int> get seq =>
      $composableBuilder(column: $table.seq, builder: (column) => column);

  GeneratedColumn<String> get id =>
      $composableBuilder(column: $table.id, builder: (column) => column);

  GeneratedColumn<String> get payload =>
      $composableBuilder(column: $table.payload, builder: (column) => column);
}

class $$JobsTableTableManager
    extends
        RootTableManager<
          _$AppDatabase,
          $JobsTable,
          FilaJob,
          $$JobsTableFilterComposer,
          $$JobsTableOrderingComposer,
          $$JobsTableAnnotationComposer,
          $$JobsTableCreateCompanionBuilder,
          $$JobsTableUpdateCompanionBuilder,
          (FilaJob, BaseReferences<_$AppDatabase, $JobsTable, FilaJob>),
          FilaJob,
          PrefetchHooks Function()
        > {
  $$JobsTableTableManager(_$AppDatabase db, $JobsTable table)
    : super(
        TableManagerState(
          db: db,
          table: table,
          createFilteringComposer: () =>
              $$JobsTableFilterComposer($db: db, $table: table),
          createOrderingComposer: () =>
              $$JobsTableOrderingComposer($db: db, $table: table),
          createComputedFieldComposer: () =>
              $$JobsTableAnnotationComposer($db: db, $table: table),
          updateCompanionCallback:
              ({
                Value<int> seq = const Value.absent(),
                Value<String> id = const Value.absent(),
                Value<String> payload = const Value.absent(),
              }) => JobsCompanion(seq: seq, id: id, payload: payload),
          createCompanionCallback:
              ({
                Value<int> seq = const Value.absent(),
                required String id,
                required String payload,
              }) => JobsCompanion.insert(seq: seq, id: id, payload: payload),
          withReferenceMapper: (p0) => p0
              .map(
                (e) => (
                  e.readTable<$JobsTable, FilaJob>(table),
                  BaseReferences<_$AppDatabase, $JobsTable, FilaJob>(
                    db,
                    table,
                    e,
                  ),
                ),
              )
              .toList(),
          prefetchHooksCallback: null,
        ),
      );
}

typedef $$JobsTableProcessedTableManager =
    ProcessedTableManager<
      _$AppDatabase,
      $JobsTable,
      FilaJob,
      $$JobsTableFilterComposer,
      $$JobsTableOrderingComposer,
      $$JobsTableAnnotationComposer,
      $$JobsTableCreateCompanionBuilder,
      $$JobsTableUpdateCompanionBuilder,
      (FilaJob, BaseReferences<_$AppDatabase, $JobsTable, FilaJob>),
      FilaJob,
      PrefetchHooks Function()
    >;
typedef $$CacheLecturasTableCreateCompanionBuilder =
    CacheLecturasCompanion Function({
      required String clave,
      required String valor,
      required int actualizadoEn,
      Value<int> rowid,
    });
typedef $$CacheLecturasTableUpdateCompanionBuilder =
    CacheLecturasCompanion Function({
      Value<String> clave,
      Value<String> valor,
      Value<int> actualizadoEn,
      Value<int> rowid,
    });

class $$CacheLecturasTableFilterComposer
    extends Composer<_$AppDatabase, $CacheLecturasTable> {
  $$CacheLecturasTableFilterComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnFilters<String> get clave => $composableBuilder(
    column: $table.clave,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get valor => $composableBuilder(
    column: $table.valor,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<int> get actualizadoEn => $composableBuilder(
    column: $table.actualizadoEn,
    builder: (column) => ColumnFilters(column),
  );
}

class $$CacheLecturasTableOrderingComposer
    extends Composer<_$AppDatabase, $CacheLecturasTable> {
  $$CacheLecturasTableOrderingComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnOrderings<String> get clave => $composableBuilder(
    column: $table.clave,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get valor => $composableBuilder(
    column: $table.valor,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<int> get actualizadoEn => $composableBuilder(
    column: $table.actualizadoEn,
    builder: (column) => ColumnOrderings(column),
  );
}

class $$CacheLecturasTableAnnotationComposer
    extends Composer<_$AppDatabase, $CacheLecturasTable> {
  $$CacheLecturasTableAnnotationComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  GeneratedColumn<String> get clave =>
      $composableBuilder(column: $table.clave, builder: (column) => column);

  GeneratedColumn<String> get valor =>
      $composableBuilder(column: $table.valor, builder: (column) => column);

  GeneratedColumn<int> get actualizadoEn => $composableBuilder(
    column: $table.actualizadoEn,
    builder: (column) => column,
  );
}

class $$CacheLecturasTableTableManager
    extends
        RootTableManager<
          _$AppDatabase,
          $CacheLecturasTable,
          FilaCache,
          $$CacheLecturasTableFilterComposer,
          $$CacheLecturasTableOrderingComposer,
          $$CacheLecturasTableAnnotationComposer,
          $$CacheLecturasTableCreateCompanionBuilder,
          $$CacheLecturasTableUpdateCompanionBuilder,
          (
            FilaCache,
            BaseReferences<_$AppDatabase, $CacheLecturasTable, FilaCache>,
          ),
          FilaCache,
          PrefetchHooks Function()
        > {
  $$CacheLecturasTableTableManager(_$AppDatabase db, $CacheLecturasTable table)
    : super(
        TableManagerState(
          db: db,
          table: table,
          createFilteringComposer: () =>
              $$CacheLecturasTableFilterComposer($db: db, $table: table),
          createOrderingComposer: () =>
              $$CacheLecturasTableOrderingComposer($db: db, $table: table),
          createComputedFieldComposer: () =>
              $$CacheLecturasTableAnnotationComposer($db: db, $table: table),
          updateCompanionCallback:
              ({
                Value<String> clave = const Value.absent(),
                Value<String> valor = const Value.absent(),
                Value<int> actualizadoEn = const Value.absent(),
                Value<int> rowid = const Value.absent(),
              }) => CacheLecturasCompanion(
                clave: clave,
                valor: valor,
                actualizadoEn: actualizadoEn,
                rowid: rowid,
              ),
          createCompanionCallback:
              ({
                required String clave,
                required String valor,
                required int actualizadoEn,
                Value<int> rowid = const Value.absent(),
              }) => CacheLecturasCompanion.insert(
                clave: clave,
                valor: valor,
                actualizadoEn: actualizadoEn,
                rowid: rowid,
              ),
          withReferenceMapper: (p0) => p0
              .map(
                (e) => (
                  e.readTable<$CacheLecturasTable, FilaCache>(table),
                  BaseReferences<_$AppDatabase, $CacheLecturasTable, FilaCache>(
                    db,
                    table,
                    e,
                  ),
                ),
              )
              .toList(),
          prefetchHooksCallback: null,
        ),
      );
}

typedef $$CacheLecturasTableProcessedTableManager =
    ProcessedTableManager<
      _$AppDatabase,
      $CacheLecturasTable,
      FilaCache,
      $$CacheLecturasTableFilterComposer,
      $$CacheLecturasTableOrderingComposer,
      $$CacheLecturasTableAnnotationComposer,
      $$CacheLecturasTableCreateCompanionBuilder,
      $$CacheLecturasTableUpdateCompanionBuilder,
      (
        FilaCache,
        BaseReferences<_$AppDatabase, $CacheLecturasTable, FilaCache>,
      ),
      FilaCache,
      PrefetchHooks Function()
    >;

class $AppDatabaseManager {
  final _$AppDatabase _db;
  $AppDatabaseManager(this._db);
  $$JobsTableTableManager get jobs => $$JobsTableTableManager(_db, _db.jobs);
  $$CacheLecturasTableTableManager get cacheLecturas =>
      $$CacheLecturasTableTableManager(_db, _db.cacheLecturas);
}
