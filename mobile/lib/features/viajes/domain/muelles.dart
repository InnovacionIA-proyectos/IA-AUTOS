import '../../catalogos/domain/catalogos.dart';

/// Los muelles activos de un lugar, en su orden. Vacía si el lugar no tiene muelles o no se conoce: entonces ni se
/// ofrece el selector ni se exige el muelle al cerrar la acomodación.
List<ValorCatalogo> muellesDelLugar(Catalogos catalogos, String? lugarId) => lugarId == null
    ? const []
    : [for (final m in catalogos.opciones(Catalogo.muelles)) if (m.campo<String>('lugar_id') == lugarId) m];
