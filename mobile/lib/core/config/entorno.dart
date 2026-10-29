/// Entornos de la app. Se eligen al compilar con `--dart-define` (o `--dart-define-from-file`); no hay URLs de
/// producción en el código. Ver docs/configuracion.md.
enum Flavor {
  dev,
  qa,
  prod;

  static Flavor desde(String valor) => Flavor.values.firstWhere((f) => f.name == valor, orElse: () => Flavor.dev);
}

class Entorno {
  const Entorno({
    required this.flavor,
    required this.apiUrl,
    this.extrasHabilitados = false,
    this.precisionBajaMetros,
    this.acomodacionFotosMinimas,
  });

  /// Lee la configuración de compilación: `FLAVOR` (dev, qa, prod) y `API_URL` (sin barra final).
  factory Entorno.desdeCompilacion() => Entorno(
        flavor: Flavor.desde(const String.fromEnvironment('FLAVOR', defaultValue: 'dev')),
        apiUrl: _sinBarraFinal(const String.fromEnvironment('API_URL')),
        extrasHabilitados: const bool.fromEnvironment('EXTRAS_HABILITADOS'),
        // 0 quiere decir «no se fijó»: entonces manda el valor inicial de la regla, que vive en el dominio.
        precisionBajaMetros: switch (const int.fromEnvironment('GPS_PRECISION_MINIMA_M')) {
          0 => null,
          final metros => metros.toDouble(),
        },
        acomodacionFotosMinimas: switch (const int.fromEnvironment('ACOMODACION_FOTOS_MINIMAS')) {
          0 => null,
          final fotos => fotos,
        },
      );

  final Flavor flavor;
  final String apiUrl;

  /// Muestra la sección de extras en la captura. Apagado por defecto: el servidor todavía no recibe extras, y
  /// con la sección visible se capturarían datos que nunca llegan.
  final bool extrasHabilitados;

  /// A partir de cuántos metros de margen del GPS se avisa de precisión baja. Es el mismo valor que el backend
  /// trae en `gps_precision_minima_m`, que la app no puede leer porque esa ruta es de administradores y del
  /// coordinador. Null si no se fijó al compilar: manda el valor inicial de la regla.
  final double? precisionBajaMetros;

  /// Cuántas fotos de la acomodación pide el servidor para cerrar la segunda inspección del cargue
  /// (`acomodacion_fotos_minimas`). La app no puede leer ese parámetro, así que se fija al compilar; null si no se
  /// fijó y manda el valor inicial de la regla. Si queda corto, el servidor rechaza el cierre y lo dice.
  final int? acomodacionFotosMinimas;

  /// Sin `API_URL` la app no puede funcionar: se muestra un aviso claro en lugar de fallar en silencio.
  bool get configurado => apiUrl.isNotEmpty;

  bool get esProduccion => flavor == Flavor.prod;

  static String _sinBarraFinal(String url) => url.trim().replaceAll(RegExp(r'/+$'), '');
}
