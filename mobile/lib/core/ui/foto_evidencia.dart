import 'package:flutter/material.dart';

import '../tema/tema.dart';

/// Una foto de evidencia servida por la API.
///
/// El enlace ya viene **firmado y con vencimiento**, así que va tal cual, sin `Authorization`: mandar además el
/// token no autoriza nada y hace creer que la foto se abre con la sesión, cuando lo que la abre es la firma.
///
/// [encabezados] es otra cosa y no contradice lo anterior: no autoriza, enruta. Detrás de la pasarela de IIS la
/// petición necesita `X-Forwarded-Host` para llegar al servicio; sin él IIS responde su propio 404 y la foto nunca
/// se pide al backend. Van vacíos cuando se llama a la API directo.
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
    this.encabezados = const {},
    this.venceEn,
    this.alVencer,
    this.ancho,
    this.sinSenal = false,
    this.semanticLabel,
    this.radio = 6,
  });

  /// El enlace firmado tal como llega. Null cuando no hay nada que mostrar.
  final String? url;

  /// Encabezados de enrutamiento de la pasarela, no de autorización. Vacíos si no hay pasarela.
  final Map<String, String> encabezados;

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
  /// Avisos porque el enlace ya venció según su propia fecha: uno por enlace. Pedir otro sí arregla esto, y el
  /// enlace nuevo viene con un vencimiento futuro, así que la cuenta no se dispara.
  bool _avisadoVencido = false;

  /// Avisos porque la imagen no cargó: **uno solo, para siempre**.
  ///
  /// Un enlace recién firmado que aun así falla no es un problema de enlace: el archivo no está en el almacén, o
  /// el almacén no responde. Pedir otro no lo va a arreglar. Y como cada refresco firma una URL distinta, la
  /// guarda por enlace se reiniciaba sola y el aviso entraba en bucle: la pantalla parpadeaba entre «buscando» y
  /// el resultado, y un teléfono en el patio llegó a hacer casi seiscientas peticiones en unos minutos.
  bool _avisadoError = false;

  bool get _vencido => switch (widget.venceEn) {
        final vence? => !vence.isAfter(DateTime.now()),
        _ => false,
      };

  @override
  void didUpdateWidget(FotoEvidencia anterior) {
    super.didUpdateWidget(anterior);
    if (anterior.url != widget.url) _avisadoVencido = false;
  }

  void _avisar({required bool porError}) {
    if (widget.alVencer == null) return;
    if (porError ? _avisadoError : _avisadoVencido) return;
    if (porError) {
      _avisadoError = true;
    } else {
      _avisadoVencido = true;
    }
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
      _avisar(porError: false);
      return _aviso('Actualizando la foto…');
    }

    return ClipRRect(
      borderRadius: BorderRadius.circular(widget.radio),
      child: Image.network(
        url,
        headers: widget.encabezados.isEmpty ? null : widget.encabezados,
        width: widget.ancho,
        height: widget.alto,
        fit: BoxFit.cover,
        semanticLabel: widget.semanticLabel,
        errorBuilder: (_, _, _) {
          // Puede ser un 403 por firma vencida —y entonces pedir otro enlace lo arregla— o que el archivo ya no
          // esté. Se intenta una vez; si el enlace nuevo también falla, se deja de pedir y se dice.
          final yaSeIntento = _avisadoError;
          _avisar(porError: true);
          return _aviso(yaSeIntento ? 'La foto ya no está disponible' : 'La foto no se pudo cargar');
        },
      ),
    );
  }
}
