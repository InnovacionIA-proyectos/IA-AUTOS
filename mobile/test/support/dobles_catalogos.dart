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
        },
      },
      usuarioId: usuarioId,
    );

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
