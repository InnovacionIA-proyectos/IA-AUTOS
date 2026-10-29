// Textos en español de los valores que la API acepta como identificadores en minúsculas.
//
// Aquí SOLO quedan los valores fijos del contrato (CONTRATO.md, «Catálogos (valores permitidos)»): no están en
// `GET /api/catalogos`, no los administra nadie desde el portal y la API no manda su nombre, así que la app es
// quien los traduce.
//
// Lo administrable —etapas, lugares, clases y tamaños de daño, niveles de severidad, vistas y cuadrantes— se
// descarga y se guarda en el dispositivo: ver `features/catalogos`. El momento de una inspección tampoco está
// aquí porque la API ya manda `momento_nombre` («Inspección de salida») listo para mostrar.

const etiquetaOperacion = <String, String>{
  'importacion': 'Importación',
  'exportacion': 'Exportación',
  'nacional': 'Nacional',
};

const etiquetaEstadoInspeccion = <String, String>{
  'borrador': 'Borrador',
  'cerrada': 'Cerrada',
};

/// Solo para el momento que **todavía no existe**: en cuanto hay inspección, la API manda `momento_nombre` y ese
/// manda. La app necesita el texto antes, para ofrecer «Capturar» en una fila que aún no tiene nada detrás.
const etiquetaMomento = <String, String>{
  'salida': 'Inspección de salida',
  'llegada': 'Inspección de llegada',
};

/// Las tres maneras de cerrar una transferencia. Cualquiera de ellas la vuelve inmutable, así que la pantalla
/// no debe ofrecer firmar sobre ninguna.
const etiquetaTipoCierre = <String, String>{
  'firmada': 'Transferencia firmada',
  'con_reservas': 'Recibida con reservas',
  'no_firma': 'Constancia de no firma',
  'recibo_sin_entrega': 'Recibo sin entrega',
};

/// Las novedades de proceso: lo que pasó alrededor de una inspección y no es un daño.
const etiquetaNovedadProceso = <String, String>{
  'cargue_sin_inspector': 'Cargue sin inspector',
  'recibo_sin_entrega': 'Recibo sin entrega',
};

const etiquetaEstadoViaje = <String, String>{
  'programado': 'Programado',
  'en_curso': 'En curso',
  'finalizado': 'Finalizado',
  'cancelado': 'Cancelado',
};

const etiquetaRol = <String, String>{
  'inspector': 'Inspector',
  'conductor': 'Conductor',
  'coordinador_calidad': 'Coordinador de calidad',
  'comercial': 'Comercial',
  'admin': 'Administrador',
  'super_admin': 'Super administrador',
};

/// Qué causó la novedad que registró el conductor en ruta. No es la clase del daño: la clase dice qué quedó en
/// la lata (golpe, rayón), esto dice qué pasó.
const etiquetaTipoNovedad = <String, String>{
  'golpe': 'Golpe',
  'piedra': 'Piedra',
  'granizo': 'Granizo',
  'incidente_vial': 'Incidente vial',
};

/// Devuelve la etiqueta del valor o, si es uno nuevo, el valor tal cual.
String etiquetar(Map<String, String> catalogo, String valor) => catalogo[valor] ?? valor;
