/// Cómo está la ubicación de un lugar del catálogo `lugares`.
///
/// Solo una ubicación confirmada sirve para la confirmación «en sitio»; una pendiente la dejó alguien con «fijar
/// ubicación aquí» y espera a que Calidad la confirme.
enum EstadoUbicacionLugar {
  sinUbicacion,
  pendienteConfirmar,
  confirmada;

  static EstadoUbicacionLugar desde(String? valor) => switch (valor) {
        'confirmada' => confirmada,
        'pendiente_confirmar' => pendienteConfirmar,
        _ => sinUbicacion,
      };
}
