import 'package:camera/camera.dart';
import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';

/// Cámara a pantalla completa. Devuelve la ruta temporal de la foto tomada o null si se cancela.
class CamaraPage extends StatefulWidget {
  const CamaraPage({super.key, this.titulo = 'Nueva foto', this.guia});

  final String titulo;

  /// Imagen de encuadre de la vista que se va a tomar (`assets/guias/...`). Null en las vistas que no tienen:
  /// entonces queda solo la etiqueta, que ya dice qué fotografiar.
  final String? guia;

  @override
  State<CamaraPage> createState() => _EstadoCamaraPage();
}

class _EstadoCamaraPage extends State<CamaraPage> {
  CameraController? _controlador;
  String? _error;
  bool _tomando = false;

  @override
  void initState() {
    super.initState();
    _iniciar();
  }

  Future<void> _iniciar() async {
    setState(() => _error = null);
    try {
      final camaras = await availableCameras();
      if (camaras.isEmpty) throw CameraException('sin_camara', 'Este dispositivo no tiene cámara.');
      final trasera = camaras.firstWhere((c) => c.lensDirection == CameraLensDirection.back, orElse: () => camaras.first);
      final controlador = CameraController(trasera, ResolutionPreset.high, enableAudio: false, imageFormatGroup: ImageFormatGroup.jpeg);
      await controlador.initialize();
      if (!mounted) {
        await controlador.dispose();
        return;
      }
      setState(() => _controlador = controlador);
    } on CameraException catch (e) {
      if (!mounted) return;
      final denegado = e.code.contains('AccessDenied');
      setState(() => _error = denegado ? 'Necesitamos la cámara para capturar la evidencia.' : (e.description ?? 'No se pudo abrir la cámara.'));
    }
  }

  Future<void> _tomar() async {
    final controlador = _controlador;
    if (controlador == null || _tomando) return;
    setState(() => _tomando = true);
    try {
      final foto = await controlador.takePicture();
      if (mounted) Navigator.of(context).pop(foto.path);
    } on CameraException {
      if (mounted) {
        setState(() => _tomando = false);
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('No se pudo tomar la foto. Inténtalo de nuevo.')));
      }
    }
  }

  @override
  void dispose() {
    _controlador?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text(widget.titulo),
        leading: TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancelar', style: TextStyle(color: Colors.white))),
        leadingWidth: 96,
      ),
      body: _contenido(),
    );
  }

  Widget _contenido() {
    if (_error != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(Espacio.xl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(_error!, textAlign: TextAlign.center, style: const TextStyle(color: Colors.white)),
              const SizedBox(height: Espacio.lg),
              FilledButton(onPressed: _iniciar, child: const Text('Permitir cámara')),
            ],
          ),
        ),
      );
    }
    final controlador = _controlador;
    if (controlador == null) return const Center(child: CircularProgressIndicator(color: Colors.white));
    return Column(
      children: [
        Expanded(
          child: Stack(
            alignment: Alignment.center,
            children: [
              Center(child: CameraPreview(controlador)),
              // Guía de encuadre encima de la vista previa: se ve el dibujo y la foto a la vez, para igualar el
              // ángulo. Translúcida y sin capturar toques, que lo que se toca es el obturador.
              if (widget.guia case final guia?)
                Positioned.fill(
                  child: IgnorePointer(
                    child: Opacity(
                      opacity: 0.35,
                      child: Image.asset(guia, fit: BoxFit.contain, semanticLabel: 'Guía de encuadre: ${widget.titulo}'),
                    ),
                  ),
                ),
            ],
          ),
        ),
        Container(
          color: Colors.black,
          padding: const EdgeInsets.symmetric(vertical: Espacio.xl),
          child: Semantics(
            button: true,
            label: 'Tomar foto',
            child: GestureDetector(
              onTap: _tomar,
              child: Container(
                width: 72,
                height: 72,
                decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: Colors.white, width: 4)),
                alignment: Alignment.center,
                child: _tomando
                    ? const SizedBox(width: 28, height: 28, child: CircularProgressIndicator(strokeWidth: 3, color: Colors.white))
                    : Container(width: 56, height: 56, decoration: const BoxDecoration(shape: BoxShape.circle, color: Colors.white)),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
