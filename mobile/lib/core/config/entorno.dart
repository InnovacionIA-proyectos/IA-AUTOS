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
    this.hostDestino = '',
    this.extrasHabilitados = false,
  });

  /// Lee la configuración de compilación: `FLAVOR` (dev, qa, prod), `API_URL` (sin barra final) y `API_HOST_DESTINO`
  /// (opcional: el servicio interno al que la pasarela de IIS debe reenviar, ver docs/configuracion.md).
  ///
  /// Las fotos mínimas de la acomodación y la precisión del GPS ya no se fijan aquí: las lee la app del servidor
  /// (`parametros_app.dart`, S8-HU-09.2 CA-1), para que un cambio de TI en el portal no pida otra compilación.
  factory Entorno.desdeCompilacion() => Entorno(
        flavor: Flavor.desde(const String.fromEnvironment('FLAVOR', defaultValue: 'dev')),
        apiUrl: _sinBarraFinal(const String.fromEnvironment('API_URL')),
        hostDestino: const String.fromEnvironment('API_HOST_DESTINO').trim(),
        extrasHabilitados: const bool.fromEnvironment('EXTRAS_HABILITADOS'),
      );

  final Flavor flavor;
  final String apiUrl;

  /// Host interno del backend cuando la API se consume a través de la pasarela `/contenedores` de IIS
  /// (`https://servicios.boterosoto.com/contenedores`). Vacío = se llama a la API directo, sin pasarela.
  final String hostDestino;

  /// Muestra la sección de extras en la captura. Los extras ya se envían con el informe (contrato 2.14.0); queda
  /// apagado por defecto hasta que el backend con esa ruta esté desplegado en el entorno: contra uno que no la tiene,
  /// el informe con extras fallaría al enviarse.
  final bool extrasHabilitados;

  /// Sin `API_URL` la app no puede funcionar: se muestra un aviso claro en lugar de fallar en silencio.
  bool get configurado => apiUrl.isNotEmpty;

  bool get esProduccion => flavor == Flavor.prod;

  /// Encabezados que toda petición a la API debe llevar para que la pasarela sepa a qué servicio reenviarla. Sin
  /// ellos IIS responde 404 (página HTML) y la petición nunca llega al backend.
  Map<String, String> get encabezadosEnrutamiento => hostDestino.isEmpty ? const {} : {'X-Forwarded-Host': hostDestino};

  static String _sinBarraFinal(String url) => url.trim().replaceAll(RegExp(r'/+$'), '');
}
