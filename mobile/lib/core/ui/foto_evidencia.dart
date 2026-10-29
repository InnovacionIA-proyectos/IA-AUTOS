import 'package:flutter/material.dart';

import '../tema/tema.dart';

/// Una foto de evidencia servida por la API.
///
/// El enlace ya viene **firmado y con vencimiento**, así que va tal cual, sin `Authorization`: mandar además el
/// token no autoriza nada y hace creer que la foto se abre con la sesión, cuando lo que la abre es la firma.
///
/// Cuando el enlace vence o el servidor lo rechaza, la imagen no se reintenta —el mismo enlace volvería a
/// fallar— : se avisa con [alVencer] para que quien la muestra vuelva a pedir su fuente, que es la que trae
/// enlaces nuevos.
///
/// Sin señal no hay nada que pedir, así que se dice, en vez de dejar un hueco gris sin explicación. La copia
/// local solo existe mientras la foto está en la cola de envío; de un informe ya enviado no queda ninguna, y
/// por eso aquí no se busca.
class FotoEvidencia extends StatefulWidget {
  const FotoEvidencia({
    super.key,
    required this.url,
    required this.alto,
    this.venceEn,
    this.alVencer,
    this.ancho,
    this.sinSenal = false,
    this.semanticLabel,
    this.radio = 6,
  });

  /// El enlace firmado tal como llega. Null cuando no hay nada que mostrar.
  final String? url;

  final double alto;
  final double? ancho;

  /// Hasta cuándo vale el enlace. Si ya pasó no se intenta pedirla: se avisa directo.
  final DateTime? venceEn;

  /// Qué hacer cuando el enlace ya no sirve: refrescar la lista o el inventario de donde salió.
  final VoidCallback? alVencer;

  final bool sinSenal;
  final String? semanticLabel;
  final double radio;

  @override
  State<FotoEvidencia> createState() => _EstadoFotoEvidencia();
}

class _EstadoFotoEvidencia extends State<FotoEvidencia> {
  /// Una sola vez por enlace: refrescar la fuente vuelve a construir este widget, y sin esto se pediría en bucle.
  bool _avisado = false;

  bool get _vencido => switch (widget.venceEn) {
        final vence? => !vence.isAfter(DateTime.now()),
        _ => false,
      };

  @override
  void didUpdateWidget(FotoEvidencia anterior) {
    super.didUpdateWidget(anterior);
    if (anterior.url != widget.url) _avisado = false;
  }

  void _avisar() {
    if (_avisado || widget.alVencer == null) return;
    _avisado = true;
    // Al terminar el fotograma: avisar en mitad del dibujo haría reconstruir lo que se está dibujando.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) widget.alVencer!();
    });
  }

  Widget _aviso(String texto) => Container(
        width: widget.ancho,
        height: widget.alto,
        color: Colores.borde,
        alignment: Alignment.center,
        padding: const EdgeInsets.all(Espacio.sm),
        child: Text(texto, textAlign: TextAlign.center, style: Theme.of(context).textTheme.bodySmall),
      );

  @override
  Widget build(BuildContext context) {
    final url = widget.url;
    if (url == null || url.isEmpty) return _aviso('Sin foto');
    if (widget.sinSenal) return _aviso('Sin conexión');
    if (_vencido) {
      _avisar();
      return _aviso('Actualizando la foto…');
    }

    return ClipRRect(
      borderRadius: BorderRadius.circular(widget.radio),
      child: Image.network(
        url,
        width: widget.ancho,
        height: widget.alto,
        fit: BoxFit.cover,
        semanticLabel: widget.semanticLabel,
        errorBuilder: (_, _, _) {
          // 403 por enlace vencido o firma inválida: el mismo enlace no va a servir, hay que pedir otro.
          _avisar();
          return _aviso('La foto no se pudo cargar');
        },
      ),
    );
  }
}
