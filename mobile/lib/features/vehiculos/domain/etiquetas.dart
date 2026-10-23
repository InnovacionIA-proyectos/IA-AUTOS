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

/// Devuelve la etiqueta del valor o, si es uno nuevo, el valor tal cual.
String etiquetar(Map<String, String> catalogo, String valor) => catalogo[valor] ?? valor;
