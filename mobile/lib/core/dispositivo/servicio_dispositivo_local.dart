import 'package:uuid/uuid.dart';

import '../db/cache_lecturas.dart';
import 'servicios.dart';

/// Identidad del equipo: un identificador propio que se genera una vez y se conserva.
///
/// No se lee nada del hardware (ni IMEI, ni número de serie, ni id de publicidad): un UUID cumple lo que esto
/// tiene que cumplir —saber desde qué equipo salió una evidencia dudosa— sin recoger un dato del dispositivo ni
/// de su dueño. Tampoco identifica a la persona: el autor del informe va en el token.
///
/// Vive con la caché y no con la sesión, porque es del equipo y debe sobrevivir a que alguien cierre sesión.
class ServicioDispositivoLocal implements ServicioDispositivo {
  ServicioDispositivoLocal(this._cache);

  static const clave = 'dispositivo.id';

  final CacheLecturasRepo _cache;
  String? _memoria;

  @override
  Future<String> id() async {
    final enMemoria = _memoria;
    if (enMemoria != null) return enMemoria;

    final guardado = await _cache.leer(clave);
    if (guardado != null && guardado.isNotEmpty) return _memoria = guardado;

    final nuevo = const Uuid().v4();
    await _cache.escribir(clave, nuevo);
    return _memoria = nuevo;
  }
}
