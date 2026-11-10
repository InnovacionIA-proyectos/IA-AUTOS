import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../core/db/cache_lecturas.dart';
import 'data/hallazgos_previos_repositorio.dart';
import 'data/inspecciones_repositorio.dart';
import 'data/novedades_repositorio.dart';
import 'domain/hallazgo_previo.dart';
import 'domain/inspeccion_resumen.dart';
import 'domain/novedad_pendiente.dart';

final cacheLecturasProvider = Provider<CacheLecturasRepo>((ref) => CacheLecturasRepo(ref.watch(appDatabaseProvider)));

final inspeccionesRepositorioProvider = Provider<InspeccionesRepositorio>(
  (ref) => InspeccionesRepositorio(ref.watch(clienteApiProvider), ref.watch(cacheLecturasProvider)),
);

final hallazgosPreviosRepositorioProvider = Provider<HallazgosPreviosRepositorio>(
  (ref) => HallazgosPreviosRepositorio(ref.watch(clienteApiProvider), ref.watch(cacheLecturasProvider)),
);

final novedadesRepositorioProvider =
    Provider<NovedadesRepositorio>((ref) => NovedadesRepositorio(ref.watch(clienteApiProvider)));

/// Con qué venía el vehículo: los daños de todas sus inspecciones cerradas, venga del viaje que venga.
///
/// La captura los pide a mano porque los necesita antes de dibujar y los conserva mientras dura el informe; la
/// ficha del VIN los mira aquí, donde se recargan solos al volver a entrar y al vencer el enlace de una foto.
///
/// Que la cola entregue algo también los deja viejos, pero eso **no** se observa desde aquí: meter el contador de
/// envíos en el cuerpo del provider lo mete en el grafo de la cola, y cuando ese contador se reinicia la pantalla
/// se queda parpadeando entre «buscando» y el resultado. Quien lo necesite lo escucha desde fuera y lo invalida.
final hallazgosPreviosProvider = FutureProvider.autoDispose.family<List<HallazgoPrevio>, String>(
  (ref, vin) => ref.watch(hallazgosPreviosRepositorioProvider).de(vin),
);

/// Los informes de un proceso de un viaje, para decir cómo va cada unidad de la lista del cargue.
final informesDelViajeProvider =
    FutureProvider.autoDispose.family<List<InspeccionResumen>, ({String viajeId, String proceso})>(
  (ref, clave) => ref.watch(inspeccionesRepositorioProvider).delViaje(clave.viajeId, proceso: clave.proceso),
);

/// Las novedades del conductor pendientes de revisión en un VIN. Se recarga al revisar una.
final novedadesPorRevisarProvider = FutureProvider.autoDispose.family<List<NovedadPendiente>, String>(
  (ref, vin) => ref.watch(novedadesRepositorioProvider).porRevisar(vin: vin),
);
