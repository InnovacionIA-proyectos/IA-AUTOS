import 'dart:io';

import '../domain/puertos.dart';

class ArchivosLocalesIo implements ArchivosLocales {
  const ArchivosLocalesIo();

  @override
  Future<List<int>> leerBytes(String ruta) => File(ruta).readAsBytes();

  @override
  Future<void> borrar(String ruta) async {
    final archivo = File(ruta);
    if (await archivo.exists()) await archivo.delete();
  }
}
