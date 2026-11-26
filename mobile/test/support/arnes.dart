import 'dart:async';
import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:traza_autos/app/proveedores.dart';
import 'package:traza_autos/core/config/entorno.dart';
import 'package:traza_autos/core/config/parametros_app.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/auth/domain/almacen_sesion.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/providers.dart';
import 'package:traza_autos/features/dibujo_dano/providers.dart';
import 'package:traza_autos/features/inspecciones/data/cierre_de_captura.dart';
import 'package:traza_autos/features/inspecciones/data/hallazgos_previos_repositorio.dart';
import 'package:traza_autos/features/inspecciones/data/inspecciones_repositorio.dart';
import 'package:traza_autos/features/inspecciones/data/novedades_repositorio.dart';
import 'package:traza_autos/features/inspecciones/domain/borrador_captura.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_previo.dart';
import 'package:traza_autos/features/inspecciones/domain/novedad_pendiente.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/inspecciones/providers.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/sync/domain/puertos.dart';
import 'package:traza_autos/features/sync/providers.dart';
import 'package:traza_autos/features/viajes/presentation/destinos_vistos.dart';
import 'package:traza_autos/features/viajes/presentation/tomados.dart';
import 'package:traza_autos/features/inspecciones/domain/puestos_guardados.dart';
import 'package:traza_autos/core/dispositivo/abridor_pdf.dart';

import 'dobles_dibujo.dart';
import 'dobles_sync.dart';

const entornoPrueba = Entorno(flavor: Flavor.dev, apiUrl: 'https://api.test');

const sesionPrueba = Sesion(
  token: 'jwt-prueba',
  usuario: Usuario(id: 'u1', nombre: 'Ana Pérez', email: 'ana@boterosoto.com.co', rol: 'inspector'),
);

/// El conductor del viaje, ya con su PIN cambiado: ve lo de sus viajes y nada más.
const sesionConductor = Sesion(
  token: 'jwt-conductor',
  usuario: Usuario(id: 'u9', nombre: 'Luis Gómez', email: '', rol: 'conductor', cedula: '1234567'),
);

/// Quien revisa los informes de su cliente sin tener viajes propios: no captura ninguno, así que pedir «solo los
/// míos» por él le deja la pestaña vacía.
const sesionCoordinador = Sesion(
  token: 'jwt-coordinadora',
  usuario: Usuario(id: 'coord-1', nombre: 'Sara Calderón', email: 'sara@trazaautos.local', rol: 'coordinador_calidad'),
);

/// Personal de campo que entró con un PIN asignado por un administrador: tiene sesión, pero el resto de la API
/// le responde 403 hasta que lo cambie.
const sesionConPinTemporal = Sesion(
  token: 'jwt-pin',
  usuario: Usuario(
    id: 'u9',
    nombre: 'Luis Gómez',
    email: '',
    rol: 'conductor',
    cedula: '1234567',
    debeCambiarPin: true,
  ),
);

class AlmacenSesionEnMemoria implements AlmacenSesion {
  AlmacenSesionEnMemoria([this.sesion]);

  Sesion? sesion;

  /// Keystore no disponible: `guardar` lanza, como puede pasar de verdad en un dispositivo.
  bool fallaAlGuardar = false;

  @override
  Future<Sesion?> leer() async => sesion;

  @override
  Future<void> guardar(Sesion s) async {
    if (fallaAlGuardar) throw Exception('almacén no disponible');
    sesion = s;
  }

  @override
  Future<void> borrar() async => sesion = null;
}

class AuthRepositorioFalso implements AuthRepositorio {
  Object? error;
  Object? errorCambioPin;

  /// Sesión que devuelve el ingreso con cédula y PIN (por ejemplo, una con `debeCambiarPin`).
  Sesion? sesionConPin;

  final List<(String, String)> intentos = [];
  final List<(String, String)> cambiosDePin = [];

  @override
  Future<Sesion> iniciarSesion(String email, String password) async {
    intentos.add((email, password));
    if (error != null) throw error!;
    return sesionPrueba;
  }

  @override
  Future<Sesion> iniciarSesionConPin(String cedula, String pin) async {
    intentos.add((cedula, pin));
    if (error != null) throw error!;
    return sesionConPin ?? sesionPrueba;
  }

  @override
  Future<void> cambiarPin(String pinActual, String pinNuevo) async {
    cambiosDePin.add((pinActual, pinNuevo));
    if (errorCambioPin != null) throw errorCambioPin!;
  }

  /// Lo que responde `GET /api/auth/me`. Con `errorCapacidades` se simula que no se pudo pedir.
  List<String> capacidadesDelPerfil = const [];
  Object? errorCapacidades;
  int consultasDePerfil = 0;

  /// El token con que se pidió el perfil. Null si se pidió sin él, que es lo que hacía que el servidor respondiera 401.
  String? tokenDelPerfil;

  @override
  Future<List<String>> capacidades({String? token}) async {
    consultasDePerfil++;
    tokenDelPerfil = token;
    if (errorCapacidades != null) throw errorCapacidades!;
    return capacidadesDelPerfil;
  }
}

/// Red controlable sin plugins: se activa/desactiva y emite cambios a mano.
class RedConectividadFalsa implements EstadoRed {
  final controlador = StreamController<bool>.broadcast();
  bool estable = true;
  bool alcanzable = true;

  @override
  Stream<bool> get cambios => controlador.stream;

  @override
  Future<bool> redEstable() async => estable;

  @override
  Future<bool> apiAlcanzable() async => alcanzable;

  void cambiarA(bool nuevo) {
    estable = nuevo;
    controlador.add(nuevo);
  }
}

class InspeccionesRepositorioFalso implements InspeccionesRepositorio {
  List<InspeccionResumen>? cache;
  List<InspeccionResumen> remoto = const [];

  /// Cuando la prueba necesita varias páginas: qué devuelve cada una. Vacío, todas devuelven [remoto].
  Map<int, List<InspeccionResumen>> paginas = const {};

  /// Cuántos dice el servidor que hay con esos filtros. Sin ponerlo, los que trae la página: una sola página y
  /// nada más que pedir.
  int? totalRemoto;
  Object? errorRemoto;
  int llamadasRemotas = 0;

  /// Con qué se pidió cada página. `usuarioId` llega nulo cuando no se mandó el filtro de autor, que es lo que
  /// distingue al coordinador de calidad del inspector.
  final peticiones = <({String? usuarioId, String? vin, int pagina})>[];

  /// Los informes que se pidieron descartar, en orden. Con [errorDescartar] puesto, el descarte lo levanta.
  final descartados = <String>[];
  Object? errorDescartar;

  @override
  Future<void> descartar(String inspeccionId) async {
    if (errorDescartar != null) throw errorDescartar!;
    descartados.add(inspeccionId);
  }

  @override
  Future<List<InspeccionResumen>?> leerCache(String usuarioId) async => cache;

  @override
  Future<PaginaInspecciones> listarRemoto(
    String usuarioId, {
    bool soloSuyas = true,
    String? vin,
    int pagina = 1,
  }) async {
    llamadasRemotas++;
    peticiones.add((usuarioId: soloSuyas ? usuarioId : null, vin: vin, pagina: pagina));
    if (errorRemoto != null) throw errorRemoto!;
    final items = paginas.isEmpty ? remoto : (paginas[pagina] ?? const <InspeccionResumen>[]);
    // Como el de verdad: solo se guarda la primera página de la lista sin buscar.
    if (pagina == 1 && (vin == null || vin.isEmpty)) cache = items;
    return PaginaInspecciones(items: items, total: totalRemoto ?? items.length);
  }

  /// Los informes del viaje, para la lista de unidades del cargue. Con qué se pidió, para comprobar que el
  /// proceso que viaja es el del pendiente y no otro.
  List<InspeccionResumen> delViajeResultado = const [];
  final consultasDeViaje = <({String viajeId, String proceso})>[];

  @override
  Future<List<InspeccionResumen>> delViaje(String viajeId, {required String proceso}) async {
    consultasDeViaje.add((viajeId: viajeId, proceso: proceso));
    if (errorRemoto != null) throw errorRemoto!;
    return delViajeResultado;
  }
}

class DispositivoFalso implements ServicioDispositivo {
  @override
  Future<String> id() async => 'equipo-de-prueba';
}

class UbicacionFalsa implements ServicioUbicacion {
  /// Por defecto, una lectura de ahora: es lo normal y es lo que se sella con su precisión.
  Posicion? posicion = Posicion(6.2, -75.5, tomadaEn: DateTime.now());
  UbicacionNoDisponible? error;

  /// El interruptor del GPS del teléfono. Como el real, solo avisa de los CAMBIOS: no emite el estado actual al
  /// suscribirse, que es lo que evita que quien lo escucha se reinvoque en bucle.
  final _gps = StreamController<bool>.broadcast();

  void encenderGps() => _gps.add(true);
  void apagarGps() => _gps.add(false);

  @override
  Future<Posicion> obtenerActual() async {
    if (error != null) throw error!;
    return posicion!;
  }

  @override
  Future<Posicion?> obtenerRapida() async => posicion;

  @override
  Stream<bool> get servicioActivo => _gps.stream;

  @override
  Future<void> abrirAjustes() async {}
}

/// Todo lo que la app necesita, sustituido por dobles en memoria.
class Arnes {
  Arnes({Sesion? sesion = sesionPrueba, this.entorno = entornoPrueba})
      : almacen = AlmacenSesionEnMemoria(sesion),
        cola = ColaEnMemoria(),
        gateway = GatewayFalso(),
        red = RedConectividadFalsa(),
        archivos = ArchivosFalsos(const []),
        auth = AuthRepositorioFalso(),
        inspecciones = InspeccionesRepositorioFalso(),
        ubicacion = UbicacionFalsa(),
        dispositivo = DispositivoFalso(),
        previos = HallazgosPreviosFalso(),
        novedades = NovedadesFalso(),
        borradores = BorradoresEnMemoria();

  final Entorno entorno;
  final AlmacenSesionEnMemoria almacen;
  final ColaEnMemoria cola;
  final GatewayFalso gateway;
  final RedConectividadFalsa red;
  final ArchivosFalsos archivos;
  final AuthRepositorioFalso auth;
  final InspeccionesRepositorioFalso inspecciones;
  final UbicacionFalsa ubicacion;
  final DispositivoFalso dispositivo;

  /// Los hallazgos previos del VIN. Vacíos por defecto: la mayoría de las pruebas no los necesitan y así la
  /// captura no pregunta nada.
  final HallazgosPreviosFalso previos;

  /// Las novedades del conductor pendientes de revisión. Vacías por defecto: casi ninguna prueba las necesita.
  final NovedadesFalso novedades;

  /// Las capturas sin cerrar guardadas en el teléfono. Vacío por defecto.
  final BorradoresEnMemoria borradores;

  /// Encolar el informe y soltar su borrador: en memoria, sobre esta misma cola y estos mismos borradores.
  late final CierreEnMemoria cierre = CierreEnMemoria(cola, borradores);

  late final ProcesadorCola procesador = ProcesadorCola(cola: cola, gateway: gateway, red: red, archivos: archivos);

  /// Lo que el conductor ya vio de cada cambio de destino (S8-HU-03.2 CA-5), en memoria.
  final destinosVistos = DestinosVistosEnMemoria();

  /// Lo último que el inspector tenía tomado, guardado en el teléfono, en memoria.
  final tomadosGuardados = TomadosEnMemoria();

  /// Los puestos de la niñera elegidos en el teléfono, en memoria.
  final puestos = PuestosEnMemoria();

  /// Los PDF que la app mandó abrir, sin tocar el teléfono.
  final pdfs = AbridorPdfFalso();

  /// Los parámetros de la app que manda el servidor. Sin nada por defecto: mandan los valores de hoy de la regla.
  final parametros = ParametrosAppFalsos();

  List<Override> get overrides => [
        almacenDestinosVistosProvider.overrideWithValue(destinosVistos),
        almacenTomadosProvider.overrideWithValue(tomadosGuardados),
        almacenPuestosProvider.overrideWithValue(puestos),
        abridorPdfProvider.overrideWithValue(pdfs),
        fuenteParametrosAppProvider.overrideWithValue(parametros),
        entornoProvider.overrideWithValue(entorno),
        almacenSesionProvider.overrideWithValue(almacen),
        authRepositorioProvider.overrideWithValue(auth),
        estadoRedProvider.overrideWithValue(red),
        colaRepositorioProvider.overrideWithValue(cola),
        procesadorColaProvider.overrideWithValue(procesador),
        inspeccionesRepositorioProvider.overrideWithValue(inspecciones),
        servicioUbicacionProvider.overrideWithValue(ubicacion),
        servicioDispositivoProvider.overrideWithValue(dispositivo),
        // Las imágenes del dibujo de daños que bajan del servidor se guardan en Drift; en las pruebas, en memoria.
        almacenImagenesPanelProvider.overrideWithValue(AlmacenImagenesEnMemoria()),
        hallazgosPreviosRepositorioProvider.overrideWithValue(previos),
        novedadesRepositorioProvider.overrideWithValue(novedades),
        almacenBorradoresProvider.overrideWithValue(borradores),
        archivosLocalesProvider.overrideWithValue(archivos),
        cierreDeCapturaProvider.overrideWithValue(cierre),
      ];

  ProviderContainer contenedor() => ProviderContainer(overrides: overrides, retry: (_, _) => null);
}

ErrorApi errorApi(int status, [String mensaje = 'error']) => ErrorApi(message: mensaje, code: 'X', status: status);

/// Hallazgos previos de mentira: se les pone la lista que la prueba quiera proponer.
class HallazgosPreviosFalso implements HallazgosPreviosRepositorio {
  List<HallazgoPrevio> lista = const [];

  @override
  Future<List<HallazgoPrevio>> de(String vin) async => lista;
}

/// Novedades del conductor de mentira: se les pone la lista que la prueba quiera mostrar.
class NovedadesFalso implements NovedadesRepositorio {
  List<NovedadPendiente> lista = const [];
  Object? errorAlRevisar;
  final List<Map<String, Object?>> revisadas = [];

  /// Lo que devuelve la lista del viaje; por defecto, lo mismo que la de un VIN.
  List<NovedadPendiente>? delViaje;
  final List<String> viajesConsultados = [];

  @override
  Future<List<NovedadPendiente>> porRevisar({required String vin, String? viajeId}) async => lista;

  @override
  Future<List<NovedadPendiente>> sinCalificarDelViaje(String viajeId) async {
    viajesConsultados.add(viajeId);
    return delViaje ?? lista;
  }

  @override
  Future<void> revisar(
    String hallazgoId, {
    required int? cuadranteNumero,
    required String? lado,
    required String clase,
    required String nivel,
    required String tamano,
    String? nota,
    bool esPreexistente = false,
    String? hallazgoOrigenId,
  }) async {
    revisadas.add({
      'id': hallazgoId,
      'cuadrante_numero': cuadranteNumero,
      'lado': lado,
      'clase': clase,
      'nivel': nivel,
      'tamano': tamano,
      'es_preexistente': esPreexistente,
      'hallazgo_origen_id': hallazgoOrigenId,
    });
    if (errorAlRevisar != null) throw errorAlRevisar!;
  }
}

/// Los borradores de captura en memoria; se guardan como JSON, así la prueba también pasa por la lectura.
class BorradoresEnMemoria implements AlmacenBorradores {
  final Map<String, Map<String, dynamic>> guardados = {};

  @override
  Future<BorradorCaptura?> leer(String clave) async =>
      guardados[clave] == null ? null : BorradorCaptura.fromJson(guardados[clave]!);

  @override
  Future<void> guardar(String clave, BorradorCaptura borrador) async =>
      guardados[clave] = jsonDecode(jsonEncode(borrador.toJson())) as Map<String, dynamic>;

  @override
  Future<void> borrar(String clave) async => guardados.remove(clave);
}

/// El cierre de una captura en memoria: las dos cosas o ninguna, como la transacción de verdad.
class CierreEnMemoria implements CierreDeCaptura {
  CierreEnMemoria(this.cola, this.borradores);

  final ColaRepositorio cola;
  final BorradoresEnMemoria borradores;

  /// Si se pone, encolar falla y no cambia nada.
  Object? error;

  @override
  Future<void> encolar(Job job, {String? claveBorrador}) async {
    if (error != null) throw error!;
    await cola.agregar(job);
    if (claveBorrador != null) await borradores.borrar(claveBorrador);
  }
}

class DestinosVistosEnMemoria implements AlmacenDestinosVistos {
  final Map<String, String> guardados = {};

  @override
  Future<String?> leer(String clave) async => guardados[clave];

  @override
  Future<void> escribir(String clave, String valor) async => guardados[clave] = valor;
}

class TomadosEnMemoria implements AlmacenTomados {
  final Map<String, String> guardados = {};

  @override
  Future<String?> leer(String clave) async => guardados[clave];

  @override
  Future<void> escribir(String clave, String valor) async => guardados[clave] = valor;
}

class PuestosEnMemoria implements AlmacenPuestos {
  final Map<String, Map<String, dynamic>> guardados = {};

  @override
  Future<PuestosGuardados?> leer(String clave) async =>
      guardados[clave] == null ? null : PuestosGuardados.fromJson(guardados[clave]!);

  @override
  Future<void> guardar(String clave, PuestosGuardados puestos) async =>
      guardados[clave] = jsonDecode(jsonEncode(puestos.toJson())) as Map<String, dynamic>;

  @override
  Future<void> borrar(String clave) async => guardados.remove(clave);
}

class AbridorPdfFalso implements AbridorPdf {
  final List<String> abiertos = [];

  /// Lo que lanza en vez de abrir: el teléfono sin app de PDF, por ejemplo.
  Object? error;

  @override
  Future<void> abrir(List<int> bytes, String nombre) async {
    if (error != null) throw error!;
    abiertos.add(nombre);
  }
}

class ParametrosAppFalsos implements FuenteParametrosApp {
  ParametrosApp? valor;

  /// Cuántas veces se pidieron al servidor: al iniciar sesión, al volver la señal y al abrir la captura.
  int lecturas = 0;

  @override
  Future<ParametrosApp?> leer() async {
    lecturas++;
    return valor;
  }

  @override
  Future<ParametrosApp?> guardados() async => valor;
}
