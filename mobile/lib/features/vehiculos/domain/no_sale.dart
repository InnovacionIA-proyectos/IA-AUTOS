/// Cuándo una unidad puede dejar de salir en un viaje, y con qué daño.
///
/// La decisión es de quien inspecciona el viaje, con un motivo de la lista (S8-HU-02.2): un daño V1+, un carro
/// que no llegó o no está listo, documentos que faltan... Las mismas reglas que el servidor aplica al recibirlo,
/// puestas aquí para no ofrecer lo que va a responder 409.
library;

import 'inventario.dart';

/// Código del nivel que rechaza el vehículo. Es un valor del contrato, no una etiqueta del catálogo.
const nivelV1Plus = 'v1_plus';

/// Los hallazgos de este VIN que permiten marcar que la unidad no sale de [viajeId]: nivel V1+, en una
/// inspección **cerrada** y **vigente** de una etapa **de ese viaje**.
///
/// Vigente quiere decir que ninguna corrección cerrada la reemplazó. Un daño que la corrección ya no trae no
/// existe: no describe el carro de hoy y no puede justificar que la unidad se quede.
List<HallazgoVista> hallazgosQueImpidenSalir(Inventario inventario, {required String viajeId}) => [
      for (final etapa in inventario.etapas)
        if (etapa.viajeId == viajeId)
          for (final inspeccion in etapa.inspecciones)
            if (inspeccion.cerrada && !etapa.fueCorregida(inspeccion))
              for (final hallazgo in inspeccion.hallazgos)
                if (hallazgo.nivel == nivelV1Plus) hallazgo,
    ];

/// Los daños que el conductor reportó en este carro durante [viajeId], con su foto y todavía sin calificar: los que
/// sirven para el motivo «Daño reportado por el conductor».
List<HallazgoVista> danosDelConductorSinCalificar(Inventario inventario, {required String viajeId}) => [
      for (final etapa in inventario.etapas)
        if (etapa.viajeId == viajeId)
          for (final inspeccion in etapa.inspecciones)
            for (final hallazgo in inspeccion.hallazgos)
              if (hallazgo.loRegistroElConductor &&
                  hallazgo.nivel == null &&
                  inspeccion.evidencias.any((e) => e.hallazgoId == hallazgo.id))
                hallazgo,
    ];

/// Si a esta persona se le ofrece marcar que la unidad no sale: el inspector de ese viaje, con cualquier motivo
/// (CA-2). El servidor responde 403 a cualquier otro rol de campo; el coordinador de calidad también puede, pero
/// trabaja desde el portal, no desde la app.
bool puedeMarcarNoSale({required String? rol, required bool esInspectorDelViaje}) =>
    rol == 'inspector' && esInspectorDelViaje;

/// Si se le ofrece «Vuelve a salir» (CA-5): el inspector del viaje, mientras la entrega del cargue no se haya
/// cerrado. Después el carro ya va en otro viaje y el servidor responde 409.
bool puedeVolverASalir({required String? rol, required bool esInspectorDelViaje, required bool cargueCerrado}) =>
    puedeMarcarNoSale(rol: rol, esInspectorDelViaje: esInspectorDelViaje) && !cargueCerrado;
