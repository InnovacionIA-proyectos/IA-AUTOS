import 'dart:convert';
import 'dart:io';

import '../domain/puertos.dart';

class ArchivosLocalesIo implements ArchivosLocales {
  const ArchivosLocalesIo();

  @override
  Future<String> leerBase64(String ruta) async => base64Encode(await File(ruta).readAsBytes());

  @override
  Future<void> borrar(String ruta) async {
    final archivo = File(ruta);
    if (await archivo.exists()) await archivo.delete();
  }
}
