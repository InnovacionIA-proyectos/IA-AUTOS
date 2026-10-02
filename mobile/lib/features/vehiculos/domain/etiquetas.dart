// Textos en español de los valores de catálogo de la API (que viajan como identificadores en minúsculas).

const etiquetaPuntoCustodia = <String, String>{
  'cargue': 'Cargue',
  'punto_origen': 'Punto Origen',
  'certificadora': 'Certificadora',
  'punto_destino': 'Punto Destino',
  'transporte': 'Transporte',
  'descargue': 'Descargue',
  'concesionario': 'Concesionario',
};

const etiquetaClaseDano = <String, String>{
  'deformacion': 'Deformación',
  'rotura': 'Rotura',
  'cortado': 'Cortado',
  'perforacion': 'Perforación',
  'golpe': 'Golpe',
  'mancha': 'Mancha',
  'raya_pintar': 'Raya para pintar',
  'desconchado': 'Desconchado',
  'particulas': 'Partículas',
  'faltante': 'Faltante',
};

const etiquetaTamanoDano = <String, String>{
  '1_5cm': 'De 1 a 5 cm',
  '5_10cm': 'De 5 a 10 cm',
  '10_20cm': 'De 10 a 20 cm',
  'mas_20cm': 'Más de 20 cm',
  'requiere_reemplazo': 'Requiere reemplazo',
  'mas_10_particulas': 'Más de 10 partículas por pieza',
};

const etiquetaMomento = <String, String>{
  'check_in': 'Check-in',
  'check_out': 'Check-out',
  'informe': 'Informe',
};

const etiquetaOperacion = <String, String>{
  'importacion': 'Importación',
  'exportacion': 'Exportación',
  'nacional': 'Nacional',
};

const etiquetaEstadoInspeccion = <String, String>{
  'borrador': 'Borrador',
  'cerrada': 'Cerrada',
};

const etiquetaRol = <String, String>{
  'inspector': 'Inspector',
  'conductor': 'Conductor',
  'coordinador_calidad': 'Coordinador de calidad',
  'comercial': 'Comercial',
  'admin': 'Administrador',
  'super_admin': 'Super administrador',
};

/// Devuelve la etiqueta del catálogo o, si el valor es nuevo, el valor tal cual.
String etiquetar(Map<String, String> catalogo, String valor) => catalogo[valor] ?? valor;
