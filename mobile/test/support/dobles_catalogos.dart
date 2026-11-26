import 'package:traza_autos/features/catalogos/data/catalogos_repositorio.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';

Catalogos catalogosCon(
  String nombre, {
  String version = 'v1',
  String usuarioId = 'u1',
  /// Vistas de foto del checklist. Vacío = la app no tiene de dónde armarlo, que es el caso del primer arranque.
  List<Map<String, dynamic>> vistas = const [],
  /// Etapas con su nombre para mostrar. Importan donde se distingue el código de su etiqueta («cargue» / «Cargue»).
  List<Map<String, dynamic>> etapas = const [],
  /// Los motivos de «No hay inspector». Vacío = no se descargaron.
  List<Map<String, dynamic>> motivos = const [],
  /// Los muelles, cada uno con su `lugar_id`. Vacío = ningún lugar tiene muelles.
  List<Map<String, dynamic>> muelles = const [],
  /// Los motivos de «no sale». Vacío = no se descargaron.
  List<Map<String, dynamic>> motivosNoSale = const [],
  /// Los tipos de la novedad del viaje (S8-HU-07.3). Vacío = no se descargaron.
  List<Map<String, dynamic>> tiposNovedadViaje = const [],
}) =>
    Catalogos.desdeRespuesta(
      {
        'version': version,
        'catalogos': {
          Catalogo.clasesDano: [
            {'id': 'i1', 'codigo': 'golpe', 'nombre': nombre, 'orden': 1, 'activo': true},
          ],
          Catalogo.tamanosDano: [
            {'id': 'i2', 'codigo': 'mas_20cm', 'nombre': 'Más de 20 cm', 'orden': 1, 'activo': true},
          ],
          // El inspector no puede registrar un daño sin nivel (el servidor responde 400), así que sin este catálogo
          // la captura no deja cerrar el informe.
          Catalogo.nivelesSeveridad: [
            {'id': 'i3', 'codigo': 'v1', 'nombre': 'V1', 'orden': 1, 'activo': true, 'accion': 'Se rechaza el vehículo'},
          ],
          Catalogo.vistasFoto: vistas,
          Catalogo.etapas: etapas,
          Catalogo.motivosSinInspector: motivos,
          Catalogo.muelles: muelles,
          Catalogo.motivosNoSale: motivosNoSale,
          Catalogo.tiposNovedadViaje: tiposNovedadViaje,
        },
      },
      usuarioId: usuarioId,
    );

/// Los tipos de la novedad del viaje como los sirve la API: de cada rol, de los dos y uno inactivo.
const tiposNovedadViajeDePrueba = [
  {'id': 't1', 'codigo': 'falla_camion', 'nombre': 'Falla del camión', 'orden': 1, 'activo': true, 'rol': 'conductor', 'urgencia': 'alta'},
  {'id': 't2', 'codigo': 'bloqueo_via', 'nombre': 'Bloqueo de vía', 'orden': 3, 'activo': true, 'rol': 'conductor', 'urgencia': 'media'},
  {'id': 't3', 'codigo': 'carro_no_llego_patio', 'nombre': 'Carro no llegó al patio', 'orden': 6, 'activo': true, 'rol': 'inspector', 'urgencia': 'media'},
  {'id': 't4', 'codigo': 'faltan_documentos', 'nombre': 'Faltan documentos', 'orden': 8, 'activo': true, 'rol': 'inspector', 'urgencia': 'media'},
  {'id': 't5', 'codigo': 'otro', 'nombre': 'Otro', 'orden': 10, 'activo': true, 'rol': 'todos', 'urgencia': 'media'},
  {'id': 't6', 'codigo': 'accidente', 'nombre': 'Accidente', 'orden': 5, 'activo': false, 'rol': 'conductor', 'urgencia': 'alta'},
];

/// Los motivos de «No hay inspector» como los sirve la API.
const motivosDePrueba = [
  {'id': 'm1', 'codigo': 'inspector_no_llego', 'nombre': 'El inspector no llegó', 'orden': 1, 'activo': true, 'requiere_texto': false},
  {'id': 'm2', 'codigo': 'otro', 'nombre': 'Otro', 'orden': 3, 'activo': true, 'requiere_texto': true},
];

/// Las etapas como las sirve la API: el código en minúscula y el nombre capitalizado.
const etapasDePrueba = [
  {'id': 'e-cargue', 'codigo': 'cargue', 'nombre': 'Cargue', 'orden': 1, 'activo': true},
  {'id': 'e-transporte', 'codigo': 'transporte', 'nombre': 'Transporte', 'orden': 2, 'activo': true},
];

/// Dos vistas del checklist: una obligatoria y una adicional, que es lo que distingue el comportamiento.
const vistasDePrueba = [
  {'id': 'v-frontal', 'codigo': 'frontal', 'nombre': 'Frontal', 'orden': 1, 'activo': true, 'obligatoria': true},
  {'id': 'v-baul', 'codigo': 'baul', 'nombre': 'Piezas en el baúl', 'orden': 2, 'activo': true, 'obligatoria': false},
];

class CatalogosRepositorioFalso implements CatalogosRepositorio {
  Catalogos? guardado;
  Catalogos? remoto;
  Object? error;
  int sincronizaciones = 0;

  @override
  Future<Catalogos?> leerCache(String usuarioId) async => guardado;

  @override
  Future<Catalogos> sincronizar(String usuarioId) async {
    sincronizaciones++;
    if (error != null) throw error!;
    guardado = remoto;
    return remoto!;
  }
}

/// Algunos motivos de «no sale» como los sirve la API, con sus tres marcas.
const motivosDePruebaNoSale = [
  {'id': 'n1', 'codigo': 'dano_v1_plus', 'nombre': 'Daño V1+', 'orden': 1, 'activo': true, 'exige_hallazgo': true, 'exige_texto': false, 'solo_sin_inspector': false},
  {'id': 'n4', 'codigo': 'faltan_documentos', 'nombre': 'Faltan documentos', 'orden': 4, 'activo': true, 'exige_hallazgo': false, 'exige_texto': false, 'solo_sin_inspector': false},
  {'id': 'n6', 'codigo': 'otro', 'nombre': 'Otro', 'orden': 6, 'activo': true, 'exige_hallazgo': false, 'exige_texto': true, 'solo_sin_inspector': false},
];
