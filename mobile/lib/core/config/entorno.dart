/// Entornos de la app. Se eligen al compilar con `--dart-define` (o `--dart-define-from-file`); no hay URLs de
/// producción en el código. Ver docs/configuracion.md.
enum Flavor {
  dev,
  qa,
  prod;

  static Flavor desde(String valor) => Flavor.values.firstWhere((f) => f.name == valor, orElse: () => Flavor.dev);
}

class Entorno {
  const Entorno({required this.flavor, required this.apiUrl});

  /// Lee la configuración de compilación: `FLAVOR` (dev, qa, prod) y `API_URL` (sin barra final).
  factory Entorno.desdeCompilacion() => Entorno(
        flavor: Flavor.desde(const String.fromEnvironment('FLAVOR', defaultValue: 'dev')),
        apiUrl: _sinBarraFinal(const String.fromEnvironment('API_URL')),
      );

  final Flavor flavor;
  final String apiUrl;

  /// Sin `API_URL` la app no puede funcionar: se muestra un aviso claro en lugar de fallar en silencio.
  bool get configurado => apiUrl.isNotEmpty;

  bool get esProduccion => flavor == Flavor.prod;

  static String _sinBarraFinal(String url) => url.trim().replaceAll(RegExp(r'/+$'), '');
}
