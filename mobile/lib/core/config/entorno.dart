/// Entornos de la app. Se eligen al compilar con `--dart-define` (o `--dart-define-from-file`); no hay URLs de
/// producción en el código. Ver docs/configuracion.md.
enum Flavor {
  dev,
  qa,
  prod;

  static Flavor desde(String valor) => Flavor.values.firstWhere((f) => f.name == valor, orElse: () => Flavor.dev);
}

class Entorno {
  const Entorno({required this.flavor, required this.apiUrl, this.hostDestino = ''});

  /// Lee la configuración de compilación: `FLAVOR` (dev, qa, prod), `API_URL` (sin barra final) y `API_HOST_DESTINO`
  /// (opcional: el servicio interno al que la pasarela de IIS debe reenviar, ver docs/configuracion.md).
  factory Entorno.desdeCompilacion() => Entorno(
        flavor: Flavor.desde(const String.fromEnvironment('FLAVOR', defaultValue: 'dev')),
        apiUrl: _sinBarraFinal(const String.fromEnvironment('API_URL')),
        hostDestino: const String.fromEnvironment('API_HOST_DESTINO').trim(),
      );

  final Flavor flavor;
  final String apiUrl;

  /// Host interno del backend cuando la API se consume a través de la pasarela `/contenedores` de IIS
  /// (`https://servicios.boterosoto.com/contenedores`). Vacío = se llama a la API directo, sin pasarela.
  final String hostDestino;

  /// Sin `API_URL` la app no puede funcionar: se muestra un aviso claro en lugar de fallar en silencio.
  bool get configurado => apiUrl.isNotEmpty;

  bool get esProduccion => flavor == Flavor.prod;

  /// Encabezados que toda petición a la API debe llevar para que la pasarela sepa a qué servicio reenviarla. Sin
  /// ellos IIS responde 404 (página HTML) y la petición nunca llega al backend.
  Map<String, String> get encabezadosEnrutamiento => hostDestino.isEmpty ? const {} : {'X-Forwarded-Host': hostDestino};

  static String _sinBarraFinal(String url) => url.trim().replaceAll(RegExp(r'/+$'), '');
}
