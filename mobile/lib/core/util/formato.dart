import 'package:intl/intl.dart';

final _formatoFecha = DateFormat('d MMM y, h:mm a', 'es_CO');

/// Fecha y hora legible en la zona horaria del dispositivo. Devuelve "—" si no hay fecha.
String formatearFecha(DateTime? fecha) => fecha == null ? '—' : _formatoFecha.format(fecha.toLocal());

/// Fecha ISO de la API a texto legible; si no se puede interpretar, devuelve el texto original.
String formatearFechaIso(String iso) {
  final fecha = DateTime.tryParse(iso);
  return fecha == null ? iso : formatearFecha(fecha);
}
