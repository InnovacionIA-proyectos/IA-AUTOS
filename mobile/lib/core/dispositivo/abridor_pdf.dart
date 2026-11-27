import 'dart:io';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

/// No se pudo mostrar el PDF, con la razón dicha para quien está en el patio.
class PdfNoAbierto implements Exception {
  const PdfNoAbierto(this.motivo);

  final String motivo;

  @override
  String toString() => motivo;
}

/// Muestra un PDF que la app ya descargó, con la app de PDF del teléfono.
abstract interface class AbridorPdf {
  /// Lanza [PdfNoAbierto] si el teléfono no lo pudo abrir.
  Future<void> abrir(List<int> bytes, String nombre);
}

/// Lo guarda en la carpeta temporal de la app (no ocupa espacio para siempre ni pide permisos) y lo abre.
class AbridorPdfDelTelefono implements AbridorPdf {
  const AbridorPdfDelTelefono();

  @override
  Future<void> abrir(List<int> bytes, String nombre) async {
    final carpeta = await getTemporaryDirectory();
    final archivo = File(p.join(carpeta.path, nombre));
    await archivo.writeAsBytes(bytes, flush: true);
    final resultado = await OpenFilex.open(archivo.path, type: 'application/pdf');
    switch (resultado.type) {
      case ResultType.done:
        return;
      case ResultType.noAppToOpen:
        throw const PdfNoAbierto('Este teléfono no tiene una app para abrir PDF.');
      case ResultType.permissionDenied:
        throw const PdfNoAbierto('El teléfono no dio permiso para abrir el PDF.');
      case ResultType.fileNotFound:
      case ResultType.error:
        throw PdfNoAbierto(
          resultado.message.isEmpty ? 'No se pudo abrir el PDF.' : 'No se pudo abrir el PDF: ${resultado.message}',
        );
    }
  }
}

final abridorPdfProvider = Provider<AbridorPdf>((ref) => const AbridorPdfDelTelefono());
