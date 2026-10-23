import '../../catalogos/domain/catalogos.dart';
import '../../vehiculos/domain/inventario.dart';
import 'foto_local.dart';

/// Una vista del checklist: qué foto hay que tomar y bajo qué condición.
///
/// Sale del catálogo `vistas-foto`, no de una lista en el código: TI y el coordinador de calidad administran
/// cuáles son y cuáles son obligatorias.
class VistaChecklist {
  const VistaChecklist({required this.id, required this.codigo, required this.nombre, required this.obligatoria});

  /// Lo que viaja en la evidencia (`vista_id`).
  final String id;

  /// Clave estable, la que usa la app para saber con qué imagen guiar la cámara.
  final String codigo;

  final String nombre;

  /// Las obligatorias se piden siempre; las demás solo si hay faltante o daño, y entonces con nota.
  final bool obligatoria;
}

/// El checklist de un VIN: las vistas activas que le corresponden, en su orden.
///
/// Las vistas **sin tipo** valen para cualquier vehículo, y a esas se suman las del tipo del VIN. Es la lectura
/// prudente de «filtradas por el tipo de vehículo del VIN o sin tipo mientras el VIN no lo tenga»: con la
/// estricta, un VIN con tipo se quedaría sin las vistas genéricas y hoy el checklist saldría vacío, porque todas
/// las sembradas vienen sin tipo. Un checklist vacío deja cerrar un informe sin una sola foto.
///
/// Hoy es teórico: el inventario todavía no trae el tipo del VIN, así que siempre se piden las sin tipo.
List<VistaChecklist> checklistDeVistas(Catalogos catalogos, {String? tipoVehiculo}) => [
      for (final valor in catalogos.opciones(Catalogo.vistasFoto))
        if (valor.codigo != null && switch (valor.campo<String>('tipo_vehiculo')) {
          null => true,
          final tipo => tipo == tipoVehiculo,
        })
          VistaChecklist(
            id: valor.id,
            codigo: valor.codigo!,
            nombre: valor.nombre,
            obligatoria: valor.campo<bool>('obligatoria') ?? false,
          ),
    ];

/// La etapa de Transporte no pide las 7 vistas: ahí la salida y la llegada son el registro del conductor, y lo
/// que se exige es el GPS de la inspección.
bool pideChecklist(String proceso) => proceso != 'transporte';

/// Las fotos de una vista, en el orden en que se tomaron. Repetir una foto no borra la anterior: se registra
/// otra evidencia de la misma vista y la última es la que cuenta.
extension FotosPorVista on List<FotoLocal> {
  List<FotoLocal> deLaVista(String vistaId) => [...where((f) => f.vistaId == vistaId)];

  FotoLocal? ultimaDeLaVista(String vistaId) => deLaVista(vistaId).lastOrNull;
}

/// Qué vistas obligatorias siguen sin foto. Es la regla de cierre del servidor, comprobada antes de enviar.
List<VistaChecklist> vistasObligatoriasSinFoto(List<VistaChecklist> checklist, List<FotoLocal> fotos) =>
    [for (final vista in checklist) if (vista.obligatoria && fotos.deLaVista(vista.id).isEmpty) vista];

/// El momento no cambia el checklist: salida y llegada piden las mismas vistas.
List<VistaChecklist> obligatoriasDe(List<VistaChecklist> checklist) => [...checklist.where((v) => v.obligatoria)];

/// Las que solo se toman si hay faltante o daño, y entonces con nota.
List<VistaChecklist> adicionalesDe(List<VistaChecklist> checklist) => [...checklist.where((v) => !v.obligatoria)];

/// Las vistas que tienen imagen guía para la cámara, por código.
///
/// Salen del prototipo de inspección de carrocería. Frontal y trasera no tienen: se toman sin guía, con la
/// etiqueta sola. «Vista superior» tampoco todavía; en el prototipo solo existe dentro de la ficha de daños, con
/// la numeración de cuadrantes, que es otra cosa (el panel de daño es S6-HU-01).
///
/// Se muestran como **referencia en una esquina**, no superpuestas a la vista previa: los dibujos tienen fondo
/// opaco y encima lavaban la cámara en vez de guiar. Para superponerlas de verdad harían falta PNG con fondo
/// transparente y sin el rótulo impreso.
const guiasDeVista = <String, String>{
  'lateral_izquierda': 'assets/guias/lateral_izquierda.png',
  'lateral_derecha': 'assets/guias/lateral_derecha.png',
  'diagonal_trasera_izquierda': 'assets/guias/diagonal_trasera_izquierda.png',
  'diagonal_delantera_derecha': 'assets/guias/diagonal_delantera_derecha.png',
};

String? guiaDe(String codigoVista) => guiasDeVista[codigoVista];

/// Las vistas no dependen del momento, pero sí de la etapa.
List<VistaChecklist> checklistDeLaEtapa(Catalogos catalogos, EtapaVista etapa, {String? tipoVehiculo}) =>
    pideChecklist(etapa.proceso) ? checklistDeVistas(catalogos, tipoVehiculo: tipoVehiculo) : const [];
