import 'inspeccion_resumen.dart';

/// Cómo va el informe de una unidad dentro de un proceso del viaje.
///
/// Es lo que necesita ver quien reclamó un cargue y tiene nueve carros delante: cuáles ya cerró, cuál dejó a
/// medias y cuáles no ha tocado. Sin esto la lista son nueve VIN iguales y hay que entrar uno por uno.
enum EstadoInformeUnidad {
  sinInforme('Sin informe'),
  borrador('En borrador'),
  cerrada('Cerrada');

  const EstadoInformeUnidad(this.etiqueta);

  /// Lo que se muestra en la insignia.
  final String etiqueta;
}

/// El estado de [vin] a partir de los informes de ese proceso del viaje.
///
/// Gana el más avanzado: una unidad con la salida cerrada y la llegada en borrador ya tiene trabajo cerrado, y
/// decir «en borrador» haría pensar que falta lo que ya está hecho. Una etapa tiene dos momentos y los dos
/// cuentan, así que no se elige por momento sino por lo más adelantado que haya.
EstadoInformeUnidad estadoDeUnidad(String vin, List<InspeccionResumen> informes) {
  final suyos = informes.where((i) => i.vin == vin);
  if (suyos.any((i) => i.estado == 'cerrada')) return EstadoInformeUnidad.cerrada;
  if (suyos.isNotEmpty) return EstadoInformeUnidad.borrador;
  return EstadoInformeUnidad.sinInforme;
}
