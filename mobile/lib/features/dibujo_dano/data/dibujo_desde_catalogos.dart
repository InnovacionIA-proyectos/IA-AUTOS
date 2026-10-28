import '../../catalogos/domain/catalogos.dart';
import '../domain/dibujo_dano.dart';

/// Arma el dibujo con lo que publica `GET /api/catalogos`, o devuelve null si el servidor todavía no publica un
/// dibujo completo. Con null la app usa la copia de los assets.
///
/// **Forma esperada (pendiente del backend; ver la guía de integración).** Hoy el servidor publica `paneles-dano`
/// (5 paneles sin tamaño de referencia) y `cuadrante-regiones` con la numeración anterior, y no publica las zonas
/// con su lado ni su nombre completo. Con eso esta función devuelve null, que es lo correcto: esos polígonos no son
/// los de las imágenes de la app. Cuando el backend cargue la versión 6, cada catálogo debe traer:
///
/// - `paneles-dano`: `codigo`, `nombre`, `orden`, `activo`, `imagen_url`, **`ancho_ref`** y **`alto_ref`**.
/// - `zonas-dano` (catálogo nuevo, de solo lectura, una fila por zona numerada de `tblZonasDano`): `numero`,
///   **`lado`** (`izquierdo`, `derecho` o null), `nombre`, **`nombre_completo`**, **`aiag_area`**, `orden`, `activo`.
/// - `cuadrante-regiones`: `panel`, `numero`, `lado`, `geometria` y **`prioridad`**.
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
    for (final z in catalogos.opciones(Catalogo.zonasDano))
      if (z.campo<num>('numero') case final numero?)
        ZonaDano(
          numero: numero.toInt(),
          lado: z.campo<String>('lado'),
          nombre: z.nombre,
          nombreCompleto: z.campo<String>('nombre_completo') ?? z.nombre,
          aiagArea: z.campo<String>('aiag_area'),
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
