import 'dart:ui';

/// Trazos de una firma dibujada. Se serializa como path SVG (`M x y L x y ...`), el formato que la web sabe
/// dibujar sin rasterizar (viewBox ajustado al trazo).
class TrazoFirma {
  TrazoFirma([Iterable<List<Offset>> trazos = const []]) : _trazos = [for (final t in trazos) List.of(t)];

  /// Reconstruye los trazos desde un path SVG guardado.
  factory TrazoFirma.desdePath(String? path) {
    if (path == null || path.trim().isEmpty) return TrazoFirma();
    final trazos = <List<Offset>>[];
    for (final m in RegExp(r'([ML])\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)').allMatches(path)) {
      final punto = Offset(double.parse(m[2]!), double.parse(m[3]!));
      if (m[1] == 'M' || trazos.isEmpty) {
        trazos.add([punto]);
      } else {
        trazos.last.add(punto);
      }
    }
    return TrazoFirma(trazos);
  }

  final List<List<Offset>> _trazos;

  List<List<Offset>> get trazos => List.unmodifiable(_trazos);
  bool get vacio => _trazos.isEmpty;

  void iniciar(Offset punto) => _trazos.add([punto]);

  void agregar(Offset punto) {
    if (_trazos.isEmpty) return iniciar(punto);
    _trazos.last.add(punto);
  }

  void limpiar() => _trazos.clear();

  /// Path SVG con un decimal de precisión, o null si no hay trazos. Un trazo de un solo punto (un toque) se
  /// conserva como un segmento mínimo para que se vea.
  String? aPathSvg() {
    if (_trazos.isEmpty) return null;
    String f(double v) => v.toStringAsFixed(1);
    final partes = <String>[];
    for (final trazo in _trazos) {
      partes.add('M ${f(trazo.first.dx)} ${f(trazo.first.dy)}');
      for (final p in trazo.skip(1)) {
        partes.add('L ${f(p.dx)} ${f(p.dy)}');
      }
      if (trazo.length == 1) partes.add('L ${f(trazo.first.dx + 0.1)} ${f(trazo.first.dy)}');
    }
    return partes.join(' ');
  }
}
