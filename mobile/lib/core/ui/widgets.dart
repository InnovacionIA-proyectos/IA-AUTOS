import 'package:flutter/material.dart';

import '../tema/tema.dart';

enum TonoInsignia { exito, advertencia, peligro, info, neutro }

/// Etiqueta de estado (Cerrada, Borrador, Requiere informe...).
class Insignia extends StatelessWidget {
  const Insignia(this.texto, {super.key, this.tono = TonoInsignia.neutro});

  final String texto;
  final TonoInsignia tono;

  @override
  Widget build(BuildContext context) {
    final (fondo, color) = switch (tono) {
      TonoInsignia.exito => (Colores.exito.withValues(alpha: 0.10), Colores.exito),
      TonoInsignia.advertencia => (Colores.advertencia.withValues(alpha: 0.15), const Color(0xFF8A5300)),
      TonoInsignia.peligro => (Colores.peligro.withValues(alpha: 0.10), Colores.peligro),
      TonoInsignia.info => (Colores.info.withValues(alpha: 0.10), Colores.info),
      TonoInsignia.neutro => (Colores.superficie, Colores.textoSecundario),
    };
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
      decoration: BoxDecoration(color: fondo, borderRadius: BorderRadius.circular(999)),
      child: Text(texto, style: TextStyle(color: color, fontSize: 13, fontWeight: FontWeight.w600)),
    );
  }
}

/// Contenedor con el estilo de tarjeta del sistema de diseño.
class Tarjeta extends StatelessWidget {
  const Tarjeta({super.key, required this.child, this.onTap, this.padding = const EdgeInsets.all(Espacio.lg)});

  final Widget child;
  final VoidCallback? onTap;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colores.superficie,
      borderRadius: BorderRadius.circular(10),
      child: InkWell(
        borderRadius: BorderRadius.circular(10),
        onTap: onTap,
        child: Container(
          width: double.infinity,
          padding: padding,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(10),
            border: Border.all(color: Colores.borde, width: 0.5),
          ),
          child: child,
        ),
      ),
    );
  }
}

/// Texto de identificadores (VIN, placa): monoespaciado para no confundir O con 0 ni I con 1.
class TextoMono extends StatelessWidget {
  const TextoMono(this.texto, {super.key, this.estilo});

  final String texto;
  final TextStyle? estilo;

  @override
  Widget build(BuildContext context) => Text(
        texto,
        style: (estilo ?? Theme.of(context).textTheme.bodyLarge)!.copyWith(fontFamily: 'monospace', fontWeight: FontWeight.w600),
      );
}

class EstadoCargando extends StatelessWidget {
  const EstadoCargando({super.key, this.mensaje});

  final String? mensaje;

  @override
  Widget build(BuildContext context) => Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const CircularProgressIndicator(),
            if (mensaje != null) ...[const SizedBox(height: Espacio.md), Text(mensaje!, style: Theme.of(context).textTheme.bodySmall)],
          ],
        ),
      );
}

/// Estado vacío o de error "educativo": explica qué pasó y qué hacer.
class EstadoMensaje extends StatelessWidget {
  const EstadoMensaje({super.key, required this.icono, required this.titulo, this.descripcion, this.colorIcono, this.accion});

  final IconData icono;
  final String titulo;
  final String? descripcion;
  final Color? colorIcono;
  final Widget? accion;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(Espacio.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icono, size: 48, color: colorIcono ?? Colores.textoSecundario),
            const SizedBox(height: Espacio.md),
            Text(titulo, style: tema.titleMedium, textAlign: TextAlign.center),
            if (descripcion != null) ...[
              const SizedBox(height: Espacio.sm),
              Text(descripcion!, style: tema.bodyMedium?.copyWith(color: Colores.textoSecundario), textAlign: TextAlign.center),
            ],
            if (accion != null) ...[const SizedBox(height: Espacio.xl), accion!],
          ],
        ),
      ),
    );
  }
}

/// Fila "etiqueta ... valor" de las tarjetas de detalle. No se muestra si no hay valor.
class CampoDetalle extends StatelessWidget {
  const CampoDetalle(this.etiqueta, this.valor, {super.key});

  final String etiqueta;
  final String? valor;

  @override
  Widget build(BuildContext context) {
    if (valor == null || valor!.isEmpty) return const SizedBox.shrink();
    final tema = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.only(top: Espacio.xs),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(etiqueta, style: tema.bodySmall),
          const SizedBox(width: Espacio.sm),
          Expanded(child: Text(valor!, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario), textAlign: TextAlign.right)),
        ],
      ),
    );
  }
}
