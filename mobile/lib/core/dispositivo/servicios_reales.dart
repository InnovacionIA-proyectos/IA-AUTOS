import 'dart:io';

import 'package:geolocator/geolocator.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

import 'servicios.dart';

class ServicioUbicacionGeolocator implements ServicioUbicacion {
  const ServicioUbicacionGeolocator();

  @override
  Future<Posicion> obtenerActual() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      throw const UbicacionNoDisponible('El GPS está apagado. Actívalo para registrar la ubicación.');
    }
    var permiso = await Geolocator.checkPermission();
    if (permiso == LocationPermission.denied) permiso = await Geolocator.requestPermission();
    if (permiso == LocationPermission.deniedForever) {
      throw const UbicacionNoDisponible('El permiso de ubicación está bloqueado. Habilítalo en los ajustes de la app.', permanente: true);
    }
    if (permiso == LocationPermission.denied) {
      throw const UbicacionNoDisponible('Necesitamos la ubicación para sellar el informe.');
    }
    try {
      final pos = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(accuracy: LocationAccuracy.high, timeLimit: Duration(seconds: 20)),
      );
      return Posicion(pos.latitude, pos.longitude, precision: pos.accuracy, tomadaEn: pos.timestamp);
    } catch (_) {
      throw const UbicacionNoDisponible('No se pudo obtener la ubicación. Verifica la señal del GPS e inténtalo de nuevo.');
    }
  }

  @override
  Future<Posicion?> obtenerRapida() async {
    try {
      final pos = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(accuracy: LocationAccuracy.medium, timeLimit: Duration(seconds: 4)),
      );
      return Posicion(pos.latitude, pos.longitude, precision: pos.accuracy, tomadaEn: pos.timestamp);
    } catch (_) {
      // La última conocida sirve para saber por dónde anda el teléfono, pero su hora es la que lleva: puede ser
      // de hace horas y de otro sitio, y quien la selle tiene que poder notarlo.
      final ultima = await Geolocator.getLastKnownPosition();
      return ultima == null
          ? null
          : Posicion(ultima.latitude, ultima.longitude, precision: ultima.accuracy, tomadaEn: ultima.timestamp);
    }
  }

  @override
  Stream<bool> get servicioActivo =>
      Geolocator.getServiceStatusStream().map((estado) => estado == ServiceStatus.enabled);

  @override
  Future<void> abrirAjustes() async {
    await Geolocator.openAppSettings();
  }
}

class ServicioFotosArchivos implements ServicioFotos {
  const ServicioFotosArchivos();

  @override
  Future<String> guardar(String rutaOrigen) async {
    final base = await getApplicationDocumentsDirectory();
    final carpeta = Directory(p.join(base.path, 'evidencias'));
    await carpeta.create(recursive: true);
    final destino = p.join(carpeta.path, 'ev_${DateTime.now().microsecondsSinceEpoch}.jpg');
    await File(rutaOrigen).copy(destino);
    return destino;
  }
}
