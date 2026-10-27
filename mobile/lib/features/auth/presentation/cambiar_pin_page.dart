import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/tema/tema.dart';
import 'auth_controller.dart';

const largoMinimoPin = 4;
const largoMaximoPin = 8;

/// Cambio de PIN obligatorio: el que trae la persona se lo asignó o reinició un administrador, y hasta cambiarlo
/// el resto de la API responde 403. No se puede salir de aquí sin cambiarlo o cerrar sesión.
class CambiarPinPage extends ConsumerStatefulWidget {
  const CambiarPinPage({super.key});

  @override
  ConsumerState<CambiarPinPage> createState() => _EstadoCambiarPinPage();
}

class _EstadoCambiarPinPage extends ConsumerState<CambiarPinPage> {
  final _actual = TextEditingController();
  final _nuevo = TextEditingController();
  final _repetido = TextEditingController();
  bool _enviando = false;
  String? _error;

  @override
  void dispose() {
    for (final c in [_actual, _nuevo, _repetido]) {
      c.dispose();
    }
    super.dispose();
  }

  bool get _largoValido => _nuevo.text.length >= largoMinimoPin && _nuevo.text.length <= largoMaximoPin;
  bool get _coinciden => _nuevo.text == _repetido.text;
  bool get _puedeEnviar => _actual.text.isNotEmpty && _largoValido && _coinciden && !_enviando;

  Future<void> _enviar() async {
    setState(() {
      _enviando = true;
      _error = null;
    });
    final error = await ref.read(authControllerProvider.notifier).cambiarPin(_actual.text, _nuevo.text);
    if (!mounted) return;
    // Si salió bien, el router saca de esta pantalla solo.
    setState(() {
      _enviando = false;
      _error = error;
    });
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final nombre = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.nombre)) ?? '';

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(Espacio.lg),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text('Cambia tu PIN', style: tema.titleLarge, textAlign: TextAlign.center),
                  const SizedBox(height: Espacio.xs),
                  Text(
                    '$nombre, tu PIN lo asignó un administrador. Elige uno propio para poder usar la app.',
                    style: tema.bodySmall,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: Espacio.xl),
                  _CampoPin(controller: _actual, etiqueta: 'PIN actual', alCambiar: () => setState(() {})),
                  const SizedBox(height: Espacio.lg),
                  _CampoPin(
                    controller: _nuevo,
                    etiqueta: 'PIN nuevo ($largoMinimoPin a $largoMaximoPin dígitos)',
                    alCambiar: () => setState(() {}),
                  ),
                  const SizedBox(height: Espacio.lg),
                  _CampoPin(
                    controller: _repetido,
                    etiqueta: 'Repite el PIN nuevo',
                    // Solo se avisa cuando ya escribió algo: no se regaña por un campo vacío.
                    error: _repetido.text.isNotEmpty && !_coinciden ? 'Los PIN no coinciden.' : null,
                    alCambiar: () => setState(() {}),
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: Espacio.lg),
                    Text(_error!, style: tema.bodySmall?.copyWith(color: Colores.peligro), textAlign: TextAlign.center),
                  ],
                  const SizedBox(height: Espacio.xl),
                  FilledButton(
                    onPressed: _puedeEnviar ? _enviar : null,
                    child: _enviando
                        ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                        : const Text('Guardar PIN'),
                  ),
                  const SizedBox(height: Espacio.sm),
                  TextButton(
                    onPressed: _enviando ? null : () => ref.read(authControllerProvider.notifier).cerrarSesion(),
                    child: const Text('Cerrar sesión'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _CampoPin extends StatelessWidget {
  const _CampoPin({required this.controller, required this.etiqueta, required this.alCambiar, this.error});

  final TextEditingController controller;
  final String etiqueta;
  final VoidCallback alCambiar;
  final String? error;

  @override
  Widget build(BuildContext context) => TextField(
        controller: controller,
        obscureText: true,
        keyboardType: TextInputType.number,
        maxLength: largoMaximoPin,
        autocorrect: false,
        enableSuggestions: false,
        inputFormatters: [FilteringTextInputFormatter.digitsOnly],
        decoration: InputDecoration(labelText: etiqueta, errorText: error, counterText: ''),
        onChanged: (_) => alCambiar(),
      );
}
