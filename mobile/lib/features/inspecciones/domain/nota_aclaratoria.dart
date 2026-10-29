/// Una nota aclaratoria sobre un registro que ya no se puede cambiar.
///
/// Un informe cerrado es evidencia y no se edita. Cuando hay algo que decir después —que la placa estaba mal,
/// que el daño se recuperó, que la foto salió movida— se agrega una nota: un registro nuevo al lado, con su
/// autor y su fecha. El original no cambia, y por eso nadie puede borrarla ni editarla.
library;

/// A qué se le puede poner una nota. Son los valores que acepta la API.
abstract final class EntidadNota {
  static const inspeccion = 'inspeccion';
  static const hallazgo = 'hallazgo';
  static const transferencia = 'transferencia';
}

/// Lo que el servidor admite en el texto.
const notaAclaratoriaMax = 1000;

class NotaAclaratoria {
  const NotaAclaratoria({
    required this.id,
    required this.texto,
    this.autorNombre,
    this.autorRol,
    this.creadoEn,
  });

  factory NotaAclaratoria.fromJson(Map<String, dynamic> json) => NotaAclaratoria(
        id: json['id'] as String,
        texto: json['texto'] as String? ?? '',
        autorNombre: json['autor_nombre'] as String?,
        autorRol: json['autor_rol'] as String?,
        creadoEn: switch (json['created_at']) { final String f => DateTime.tryParse(f), _ => null },
      );

  final String id;
  final String texto;

  /// Quién la escribió, ya resuelto por la API: una nota sin autor no aclara nada.
  final String? autorNombre;
  final String? autorRol;
  final DateTime? creadoEn;
}
