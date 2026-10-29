/// Catálogos maestros descargados de `GET /api/catalogos`. Son administrables desde el portal, así que la app NO
/// los lleva fijos en el código: guarda la última copia y la usa aunque no haya señal.
///
/// Los valores son heterogéneos (`vistas-foto` trae `obligatoria` y `tipo_vehiculo`, `niveles-severidad` trae
/// `accion`, `cuadrantes` trae `numero` y no trae `codigo`, `cuadrante-regiones` trae `geometria`), por eso se
/// guarda el ítem completo y no un mapa de código a texto: cada pantalla lee los campos que necesita.
library;

/// Nombres de los catálogos tal como los sirve la API y como se usan de clave.
abstract final class Catalogo {
  static const etapas = 'etapas';
  static const puntosCustodia = 'puntos-custodia';
  static const tiposUnidad = 'tipos-unidad';
  static const clasesDano = 'clases-dano';
  static const tamanosDano = 'tamanos-dano';
  static const nivelesSeveridad = 'niveles-severidad';
  static const vistasFoto = 'vistas-foto';
  static const cuadrantes = 'cuadrantes';

  /// Dibujo de daños: los paneles (con `imagen_url`) y el polígono de cada zona en cada panel. `cuadrante-vistas`
  /// ya no existe en la API (responde 404); lo reemplazan estos dos.
  static const panelesDano = 'paneles-dano';
  static const cuadranteRegiones = 'cuadrante-regiones';

  /// Las zonas numeradas (cuadrante + lado) con su nombre completo y su área AIAG. **Todavía no lo publica la API**:
  /// es la propuesta para el backend (ver la guía de integración del dibujo de daños). Mientras no llegue, la app
  /// dibuja con la copia de los assets.
  static const zonasDano = 'zonas-dano';

  /// Tipos de extra: `barras_techo`, `volco`, `platon` y `otro`. Los administra Calidad. Sin ellos, la captura ofrece
  /// solo «Otro».
  static const tiposExtra = 'tipos-extra';
  static const clientes = 'clientes';

  /// Por qué no hay inspector en un cargue: `inspector_no_llego`, `inspector_no_disponible` y `otro` (que exige texto).
  static const motivosSinInspector = 'motivos-sin-inspector';
}

/// Catálogos que hoy la API sirve iguales para todos, pero que podría pasar a filtrar por el cliente del usuario.
/// Si la copia guardada la descargó otra persona, estos no se entregan desde la caché (ver [Catalogos.desdeAlmacen]).
const catalogosPorUsuario = {Catalogo.clientes};

/// Un valor de catálogo tal como lo sirve la API: `id`, `codigo`, `nombre`, `orden`, `activo` y los campos propios
/// de su catálogo. Se conserva el mapa completo para no perder los campos que todavía no se usan.
class ValorCatalogo {
  const ValorCatalogo(this.campos);

  final Map<String, dynamic> campos;

  String get id => campos['id'] as String? ?? '';

  /// Clave estable del valor (lo que viaja en el resto de la API). Las zonas numeradas de `cuadrantes` y las
  /// `cuadrante-regiones` no la tienen.
  String? get codigo => campos['codigo'] as String?;

  /// Etiqueta para mostrar. Puede cambiar; nunca se usa como clave.
  String get nombre => campos['nombre'] as String? ?? codigo ?? '';

  int get orden => (campos['orden'] as num?)?.toInt() ?? 0;

  /// Un valor inactivo ya no se ofrece en los selectores, pero sigue siendo válido en lo ya guardado.
  bool get activo => campos['activo'] as bool? ?? true;

  /// Campo propio del catálogo (`obligatoria`, `accion`, `numero`, `tipo_vehiculo`, `geometria`...).
  T? campo<T>(String nombre) => campos[nombre] is T ? campos[nombre] as T : null;

  Map<String, dynamic> toJson() => campos;
}

/// Copia inmutable de los catálogos, con su versión (el `ETag` del servidor).
///
/// El índice por código se arma una sola vez al construirla: las pantallas llaman a [etiqueta] en cada `build`, así
/// que la búsqueda debe ser directa y no recorrer listas.
class Catalogos {
  Catalogos({
    required this.version,
    required this.usuarioId,
    required Map<String, List<ValorCatalogo>> porNombre,
    String? etag,
  })  : etag = etag == null || etag.isEmpty ? (version.isEmpty ? '' : '"$version"') : etag,
        _porNombre = porNombre,
        _porCodigo = {
          for (final entrada in porNombre.entries)
            // Los valores sin código (cuadrantes numerados, cuadrante-regiones) quedan fuera del índice: no se buscan así.
            entrada.key: {for (final valor in entrada.value) ?valor.codigo: valor},
        };

  /// Copia de la API (`{version, catalogos}`), sellada con el usuario que la descargó.
  factory Catalogos.desdeRespuesta(Map<String, dynamic> json, {required String usuarioId, String? etag}) =>
      Catalogos(
        version: json['version'] as String? ?? '',
        usuarioId: usuarioId,
        porNombre: _leerCatalogos(json['catalogos']),
        etag: etag,
      );

  /// Copia guardada en el dispositivo. Si la descargó otra persona, se omiten los catálogos de
  /// [catalogosPorUsuario]: el día que la API los filtre por cliente, un usuario no verá los del anterior.
  factory Catalogos.desdeAlmacen(Map<String, dynamic> json, {required String usuarioId}) {
    final dueno = json['usuarioId'] as String?;
    final leidos = _leerCatalogos(json['catalogos']);
    if (dueno != usuarioId) {
      leidos.removeWhere((nombre, _) => catalogosPorUsuario.contains(nombre));
    }
    return Catalogos(
      version: json['version'] as String? ?? '',
      usuarioId: dueno ?? '',
      porNombre: leidos,
      etag: json['etag'] as String?,
    );
  }

  /// Sin copia descargada. `etiqueta()` devuelve el código tal cual y los selectores no tienen opciones, que es
  /// justo lo que debe bloquear la captura.
  static final vacio = Catalogos(version: '', usuarioId: '', porNombre: const {});

  final String version;

  /// Validador que se devuelve en `If-None-Match`, tal como lo mando el servidor (entre comillas y, si un proxy lo
  /// reescribio, con el sufijo que le haya puesto). Si la respuesta no trajo `ETag` se arma con la version, que es
  /// el mismo valor segun el contrato.
  final String etag;

  /// Quién descargó esta copia. No particiona el almacenamiento (la clave es única y global): solo permite
  /// detectar que la copia es de otra persona.
  final String usuarioId;

  final Map<String, List<ValorCatalogo>> _porNombre;
  final Map<String, Map<String, ValorCatalogo>> _porCodigo;

  /// Valores que se ofrecen en un selector: solo los activos, en su orden.
  ///
  /// Se ordena aquí aunque la API ya los mande ordenados: así el orden de la pantalla no depende de una promesa
  /// del servidor que nada comprueba.
  List<ValorCatalogo> opciones(String catalogo) => _ordenados(todos(catalogo).where((v) => v.activo));

  /// Opciones que además sirven como respuesta: las que tienen `codigo`, que es lo que se guarda en un hallazgo.
  /// Los cuadrantes numerados y las regiones no lo traen, y sin esto dos valores sin código se tratarían como el mismo.
  List<ValorCatalogo> seleccionables(String catalogo) => [...opciones(catalogo).where((v) => v.codigo != null)];

  /// Todos los valores, activos e inactivos. Es lo que hay que usar para dibujar informes viejos: un hallazgo
  /// antiguo puede tener una clase que hoy está inactiva.
  List<ValorCatalogo> todos(String catalogo) => [...?_porNombre[catalogo]];

  /// Valor por su código, activo o no; null si el catálogo o el código no existen.
  ValorCatalogo? valor(String catalogo, String codigo) => _porCodigo[catalogo]?[codigo];

  /// Etiqueta de un código. Busca entre todos (un informe viejo puede traer un valor ya inactivo) y, si no lo
  /// encuentra, devuelve el código tal cual: igual que hacía `etiquetar()` con los mapas fijos.
  String etiqueta(String catalogo, String codigo) => valor(catalogo, codigo)?.nombre ?? codigo;

  Map<String, dynamic> toJson() => {
        'version': version,
        'etag': etag,
        'usuarioId': usuarioId,
        'catalogos': {
          for (final entrada in _porNombre.entries) entrada.key: [for (final v in entrada.value) v.toJson()],
        },
      };

  /// Ordena por `orden` **conservando el orden del servidor en los empates**. `List.sort` de Dart no es estable y
  /// `orden` tiene 0 por defecto en la base, así que varios valores creados sin ponerlo empatan: sin desempatar por
  /// la posición original, los chips saldrían barajados y en distinto orden cada vez.
  static List<ValorCatalogo> _ordenados(Iterable<ValorCatalogo> valores) {
    final numerados = [...valores.indexed];
    numerados.sort((a, b) {
      final porOrden = a.$2.orden.compareTo(b.$2.orden);
      return porOrden != 0 ? porOrden : a.$1.compareTo(b.$1);
    });
    return [for (final (_, valor) in numerados) valor];
  }

  static Map<String, List<ValorCatalogo>> _leerCatalogos(Object? crudo) {
    if (crudo is! Map) return {};
    return {
      for (final entrada in crudo.entries)
        if (entrada.value is List)
          entrada.key as String: [
            for (final item in entrada.value as List)
              if (item is Map<String, dynamic>) ValorCatalogo(item),
          ],
    };
  }
}
