import '../../../core/http/cliente_api.dart';
import '../domain/aviso.dart';

/// La bandeja de avisos. Va directo a la API, sin cola ni copia: lo que dice cada aviso lo decide el servidor, y
/// responder una novedad es algo de lo que quien responde tiene que enterarse ahí mismo (la primera respuesta es
/// la única que se guarda).
class AvisosRepositorio {
  AvisosRepositorio(this._api);

  final ClienteApi _api;

  /// Una página de la bandeja, los más recientes primero. Cada persona ve solo los suyos.
  Future<PaginaAvisos> pagina({int pagina = 1, int tamano = 50}) async {
    final r = await _api.get('/api/notificaciones', query: {'pagina': pagina, 'tamano': tamano});
    return PaginaAvisos.fromJson(r as Map<String, dynamic>);
  }

  /// Lo marca como leído. Solo su destinatario puede; abrirlo otra vez no cambia la primera hora.
  Future<Aviso> marcarLeido(String id) async {
    final r = await _api.post('/api/notificaciones/${Uri.encodeComponent(id)}/leida');
    return Aviso.fromJson(r as Map<String, dynamic>);
  }

  /// Responde una novedad del conductor. Solo la primera respuesta se guarda: otra, aunque llegue a la vez desde otro
  /// coordinador, responde 409 y conserva la primera.
  Future<Aviso> responder(String id, {required String texto, required String medio}) async {
    final r = await _api.post(
      '/api/notificaciones/${Uri.encodeComponent(id)}/respuesta',
      cuerpo: {'texto': texto, 'medio': medio},
    );
    return Aviso.fromJson(r as Map<String, dynamic>);
  }
}
