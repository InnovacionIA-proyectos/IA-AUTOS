import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:traza_autos/app/proveedores.dart';
import 'package:traza_autos/core/config/entorno.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/seguridad/almacen_sesion.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/inspecciones/data/inspecciones_repositorio.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/sync/domain/puertos.dart';

import 'dobles_sync.dart';

const entornoPrueba = Entorno(flavor: Flavor.dev, apiUrl: 'https://api.test');

const sesionPrueba = Sesion(
  token: 'jwt-prueba',
  usuario: Usuario(id: 'u1', nombre: 'Ana Pérez', email: 'ana@boterosoto.com.co', rol: 'inspector'),
);

class AlmacenSesionEnMemoria implements AlmacenSesion {
  AlmacenSesionEnMemoria([this.sesion]);

  Sesion? sesion;

  @override
  Future<Sesion?> leer() async => sesion;

  @override
  Future<void> guardar(Sesion s) async => sesion = s;

  @override
  Future<void> borrar() async => sesion = null;
}

class AuthRepositorioFalso implements AuthRepositorio {
  Object? error;
  final List<(String, String)> intentos = [];

  @override
  Future<Sesion> iniciarSesion(String email, String password) async {
    intentos.add((email, password));
    if (error != null) throw error!;
    return sesionPrueba;
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
  Object? errorRemoto;
  int llamadasRemotas = 0;

  @override
  Future<List<InspeccionResumen>?> leerCache(String usuarioId) async => cache;

  @override
  Future<List<InspeccionResumen>> listarRemoto(String usuarioId) async {
    llamadasRemotas++;
    if (errorRemoto != null) throw errorRemoto!;
    cache = remoto;
    return remoto;
  }
}

class UbicacionFalsa implements ServicioUbicacion {
  Posicion? posicion = const Posicion(6.2, -75.5);
  UbicacionNoDisponible? error;

  @override
  Future<Posicion> obtenerActual() async {
    if (error != null) throw error!;
    return posicion!;
  }

  @override
  Future<Posicion?> obtenerRapida() async => posicion;

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
        ubicacion = UbicacionFalsa();

  final Entorno entorno;
  final AlmacenSesionEnMemoria almacen;
  final ColaEnMemoria cola;
  final GatewayFalso gateway;
  final RedConectividadFalsa red;
  final ArchivosFalsos archivos;
  final AuthRepositorioFalso auth;
  final InspeccionesRepositorioFalso inspecciones;
  final UbicacionFalsa ubicacion;

  late final ProcesadorCola procesador = ProcesadorCola(cola: cola, gateway: gateway, red: red, archivos: archivos);

  List<Override> get overrides => [
        entornoProvider.overrideWithValue(entorno),
        almacenSesionProvider.overrideWithValue(almacen),
        authRepositorioProvider.overrideWithValue(auth),
        estadoRedProvider.overrideWithValue(red),
        colaRepositorioProvider.overrideWithValue(cola),
        procesadorColaProvider.overrideWithValue(procesador),
        inspeccionesRepositorioProvider.overrideWithValue(inspecciones),
        servicioUbicacionProvider.overrideWithValue(ubicacion),
      ];

  ProviderContainer contenedor() => ProviderContainer(overrides: overrides, retry: (_, _) => null);
}

ErrorApi errorApi(int status, [String mensaje = 'error']) => ErrorApi(message: mensaje, code: 'X', status: status);
