/// Cuándo una unidad puede dejar de salir en un viaje, y con qué hallazgo.
///
/// La decisión es de quien inspecciona el viaje: un daño V1+ se rechaza, y la unidad se queda sin bloquear el
/// viaje de las demás. Las mismas reglas que el servidor aplica al recibirlo, puestas aquí para no ofrecer un
/// botón que va a responder 409.
library;

import 'inventario.dart';

/// Código del nivel que rechaza el vehículo. Es un valor del contrato, no una etiqueta del catálogo.
const nivelV1Plus = 'v1_plus';

/// Los hallazgos de este VIN que permiten marcar que la unidad no sale de [viajeId]: nivel V1+, en una
/// inspección **cerrada** de una etapa **de ese viaje**.
List<HallazgoVista> hallazgosQueImpidenSalir(Inventario inventario, {required String viajeId}) => [
      for (final etapa in inventario.etapas)
        if (etapa.viajeId == viajeId)
          for (final inspeccion in etapa.inspecciones)
            if (inspeccion.cerrada)
              for (final hallazgo in inspeccion.hallazgos)
                if (hallazgo.nivel == nivelV1Plus) hallazgo,
    ];

/// Si a esta persona se le ofrece marcar que la unidad no sale.
///
/// Solo el inspector de ese viaje: el servidor responde 403 a cualquier otro rol de campo. El coordinador de
/// calidad también puede, pero trabaja desde el portal, no desde la app.
bool puedeMarcarNoSale({
  required String? rol,
  required bool esInspectorDelViaje,
  required bool hayHallazgoV1Plus,
}) =>
    rol == 'inspector' && esInspectorDelViaje && hayHallazgoV1Plus;
