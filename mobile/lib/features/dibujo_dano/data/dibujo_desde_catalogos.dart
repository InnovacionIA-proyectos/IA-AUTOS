import '../../catalogos/domain/catalogos.dart';
import '../domain/dibujo_dano.dart';

/// Arma el dibujo con lo que publica `GET /api/catalogos`, o devuelve null si el servidor todavía no publica un
/// dibujo completo. Con null la app usa la copia de los assets.
///
/// **De dónde sale cada cosa** (comprobado contra la API el 3-nov-2026, catálogo versión 6):
///
/// - `paneles-dano`: `codigo`, `nombre`, `orden`, `activo`, `imagen_url`, `ancho_ref` y `alto_ref`.
/// - `cuadrantes`: `numero`, `nombre`, `lleva_lado` y **`zonas`**, un arreglo con una entrada por lado
///   (`lado`, `nombre_completo`, `aiag_area`). Las zonas no son un catálogo aparte: viven dentro de su cuadrante,
///   y de ahí salen las 80 que usa el dibujo (45 números, 35 con los dos lados y 10 centrales).
/// - `cuadrante-regiones`: `panel`, `numero`, `lado`, `geometria` y `prioridad`.
///
/// Un cuadrante inactivo se lleva sus zonas y, con ellas, sus regiones: es como el portal retira una pieza sin
/// borrar lo ya registrado con ella.
///
/// Si al backend le conviene otra forma, solo cambia esta función: el resto de la app recibe un [DibujoDano].
///
/// [imagenes] trae, por código de panel, los bytes ya guardados de su imagen; un panel sin imagen hace que se
/// descarte todo el dibujo del servidor (sus polígonos solo valen sobre su imagen).
DibujoDano? dibujoDesdeCatalogos(Catalogos catalogos, Map<String, ImagenPanel> imagenes) {
  final paneles = <PanelDano>[];
  for (final p in catalogos.opciones(Catalogo.panelesDano)) {
    final codigo = p.codigo;
    final ancho = p.campo<num>('ancho_ref');
    final alto = p.campo<num>('alto_ref');
    final imagen = codigo == null ? null : imagenes[codigo];
    if (codigo == null || ancho == null || alto == null || ancho <= 0 || alto <= 0 || imagen == null) return null;
    paneles.add(PanelDano(
      codigo: codigo,
      nombre: p.nombre,
      anchoRef: ancho.toDouble(),
      altoRef: alto.toDouble(),
      imagen: imagen,
    ));
  }

  final zonas = <ZonaDano>[
    for (final c in catalogos.opciones(Catalogo.cuadrantes))
      if (c.campo<num>('numero') case final numero?)
        for (final z in zonasDelCuadrante(c))
          ZonaDano(
            numero: numero.toInt(),
            // El lado sale del dato, nunca del número: la numeración la decide Calidad y ya cambió una vez.
            lado: z['lado'] as String?,
            nombre: c.nombre,
            nombreCompleto: z['nombre_completo'] as String? ?? c.nombre,
            aiagArea: z['aiag_area'] as String?,
          ),
  ];
  if (paneles.isEmpty || zonas.isEmpty) return null;

  final activos = {for (final p in paneles) p.codigo};
  final claves = {for (final z in zonas) z.clave};
  final regiones = <({String panel, int numero, String? lado, int prioridad, List<Punto> puntos})>[];
  for (final r in catalogos.todos(Catalogo.cuadranteRegiones)) {
    final panel = r.campo<String>('panel');
    final numero = r.campo<num>('numero');
    if (panel == null || numero == null) return null;
    final lado = r.campo<String>('lado');
    // Las regiones de un panel o de una zona inactivos no se dibujan: es como el portal retira algo sin borrarlo.
    if (!activos.contains(panel) || !claves.contains(claveDeZona(numero.toInt(), lado))) continue;
    try {
      regiones.add((
        panel: panel,
        numero: numero.toInt(),
        lado: lado,
        prioridad: r.campo<num>('prioridad')?.toInt() ?? 1,
        puntos: leerPuntos(r.campo<Object>('geometria')),
      ));
    } on FormatException {
      return null;
    }
  }

  try {
    return DibujoDano.armar(
      version: catalogos.version,
      origen: OrigenDibujo.servidor,
      paneles: paneles,
      zonas: zonas,
      regiones: regiones,
    );
  } on FormatException {
    return null;
  }
}

/// Las zonas que trae un cuadrante del catálogo, o vacío si no las trae. Un cuadrante con `lleva_lado` debería
/// traer dos; no se comprueba aquí, porque eso lo decide el portal y la app dibuja lo que haya.
List<Map<String, dynamic>> zonasDelCuadrante(ValorCatalogo cuadrante) => [
      for (final z in cuadrante.campo<List<dynamic>>('zonas') ?? const []) if (z is Map<String, dynamic>) z,
    ];
