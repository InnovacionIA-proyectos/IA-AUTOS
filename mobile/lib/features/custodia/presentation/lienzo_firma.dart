import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../domain/trazo_firma.dart';

/// Lienzo de firma dibujada. Reporta el trazo como path SVG (o null si está vacío) y no rasteriza.
class LienzoFirma extends StatefulWidget {
  const LienzoFirma({super.key, required this.etiqueta, required this.onCambio});

  final String etiqueta;
  final ValueChanged<String?> onCambio;

  @override
  State<LienzoFirma> createState() => _EstadoLienzoFirma();
}

class _EstadoLienzoFirma extends State<LienzoFirma> {
  final _trazo = TrazoFirma();

  void _notificar() => widget.onCambio(_trazo.aPathSvg());

  void _limpiar() {
    setState(_trazo.limpiar);
    _notificar();
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(widget.etiqueta, style: tema.titleMedium?.copyWith(fontSize: 15)),
            TextButton(onPressed: _limpiar, child: const Text('Limpiar')),
          ],
        ),
        // El lienzo reclama el gesto de inmediato: sin esto, arrastrar en vertical haría desplazar la pantalla.
        RawGestureDetector(
          gestures: {EagerGestureRecognizer: GestureRecognizerFactoryWithHandlers<EagerGestureRecognizer>(EagerGestureRecognizer.new, (_) {})},
          child: Listener(
            onPointerDown: (e) => setState(() => _trazo.iniciar(e.localPosition)),
            onPointerMove: (e) => setState(() => _trazo.agregar(e.localPosition)),
            onPointerUp: (_) => _notificar(),
            onPointerCancel: (_) => _notificar(),
            child: Semantics(
              label: widget.etiqueta,
              child: Container(
                height: 160,
                width: double.infinity,
                decoration: BoxDecoration(
                  color: Colores.fondo,
                  border: Border.all(color: Colores.borde),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(10),
                  child: CustomPaint(
                    painter: _PintorFirma(_trazo.trazos),
                    child: _trazo.vacio
                        ? Center(child: Text('Firme aquí', style: tema.bodySmall))
                        : const SizedBox.expand(),
                  ),
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _PintorFirma extends CustomPainter {
  _PintorFirma(List<List<Offset>> trazos) : _trazos = [for (final t in trazos) List.of(t)];

  final List<List<Offset>> _trazos;

  @override
  void paint(Canvas canvas, Size size) {
    final pincel = Paint()
      ..color = Colores.textoPrimario
      ..strokeWidth = 2.5
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round;
    for (final trazo in _trazos) {
      if (trazo.length == 1) {
        canvas.drawCircle(trazo.first, 1.25, pincel..style = PaintingStyle.fill);
        pincel.style = PaintingStyle.stroke;
        continue;
      }
      final path = Path()..moveTo(trazo.first.dx, trazo.first.dy);
      for (final p in trazo.skip(1)) {
        path.lineTo(p.dx, p.dy);
      }
      canvas.drawPath(path, pincel);
    }
  }

  @override
  bool shouldRepaint(_PintorFirma anterior) => true;
}
