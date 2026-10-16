import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import 'data/archivos_locales_io.dart';
import 'data/cola_drift.dart';
import 'data/estado_red_conectividad.dart';
import 'data/gateway_informes_api.dart';
import 'domain/procesador_cola.dart';
import 'domain/puertos.dart';

final estadoRedProvider = Provider<EstadoRed>((ref) => EstadoRedConectividad(apiUrl: ref.watch(entornoProvider).apiUrl));

final colaRepositorioProvider = Provider<ColaRepositorio>((ref) => ColaDrift(ref.watch(appDatabaseProvider)));

final procesadorColaProvider = Provider<ProcesadorCola>(
  (ref) => ProcesadorCola(
    cola: ref.watch(colaRepositorioProvider),
    gateway: GatewayInformesApi(ref.watch(clienteApiProvider)),
    red: ref.watch(estadoRedProvider),
    archivos: const ArchivosLocalesIo(),
  ),
);
