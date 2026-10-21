import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/tema/tema.dart';
import 'auth_controller.dart';
import 'cambiar_pin_page.dart' show largoMaximoPin, largoMinimoPin;

/// Cómo entra la persona. El personal de campo (conductor e inspector) puede no tener correo, así que entra con
/// cédula y PIN; quien administra entra con correo y contraseña.
enum ModoIngreso { pin, correo }

class LoginPage extends ConsumerStatefulWidget {
  const LoginPage({super.key});

  @override
  ConsumerState<LoginPage> createState() => _EstadoLoginPage();
}

class _EstadoLoginPage extends ConsumerState<LoginPage> {
  // De campo por defecto: es quien más entra a esta app.
  ModoIngreso _modo = ModoIngreso.pin;

  final _cedula = TextEditingController();
  final _pin = TextEditingController();
  final _correo = TextEditingController();
  final _clave = TextEditingController();
  bool _entrando = false;
  bool _verClave = false;
  String? _error;

  @override
  void dispose() {
    for (final c in [_cedula, _pin, _correo, _clave]) {
      c.dispose();
    }
    super.dispose();
  }

  bool get _puedeEnviar =>
      !_entrando &&
      switch (_modo) {
        ModoIngreso.pin => _cedula.text.trim().isNotEmpty && _pin.text.length >= largoMinimoPin,
        ModoIngreso.correo => _correo.text.trim().contains('@') && _clave.text.isNotEmpty,
      };

  void _cambiarModo(ModoIngreso modo) => setState(() {
        _modo = modo;
        _error = null;
      });

  Future<void> _enviar() async {
    setState(() {
      _entrando = true;
      _error = null;
    });
    final controlador = ref.read(authControllerProvider.notifier);
    final error = switch (_modo) {
      ModoIngreso.pin => await controlador.iniciarSesionConPin(_cedula.text, _pin.text),
      ModoIngreso.correo => await controlador.iniciarSesion(_correo.text, _clave.text),
    };
    if (!mounted) return;
    // Si entró bien, el router redirige solo; este widget deja de existir.
    setState(() {
      _entrando = false;
      _error = error;
    });
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final entorno = ref.watch(entornoProvider);
    final aviso = ref.watch(authControllerProvider.select((e) => e.aviso));

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
                  Image.asset('assets/logo.png', height: 110, semanticLabel: 'Botero Soto'),
                  const SizedBox(height: Espacio.sm),
                  Text('Traza Autos 360°', style: tema.titleLarge, textAlign: TextAlign.center),
                  const SizedBox(height: Espacio.xs),
                  Text('Ingresa para registrar inspecciones en campo.', style: tema.bodySmall, textAlign: TextAlign.center),
                  const SizedBox(height: Espacio.xl),
                  if (!entorno.configurado) ...[
                    _Aviso(
                      texto: 'La app no tiene configurada la dirección de la API (API_URL). Compila con --dart-define-from-file.',
                      color: Colores.peligro,
                    ),
                    const SizedBox(height: Espacio.lg),
                  ] else if (aviso != null) ...[
                    _Aviso(texto: aviso, color: Colores.advertencia),
                    const SizedBox(height: Espacio.lg),
                  ],
                  SegmentedButton<ModoIngreso>(
                    segments: const [
                      ButtonSegment(value: ModoIngreso.pin, label: Text('Cédula y PIN'), icon: Icon(Icons.badge_outlined)),
                      ButtonSegment(value: ModoIngreso.correo, label: Text('Correo'), icon: Icon(Icons.alternate_email)),
                    ],
                    selected: {_modo},
                    onSelectionChanged: (s) => _cambiarModo(s.first),
                  ),
                  const SizedBox(height: Espacio.lg),
                  ...switch (_modo) {
                    ModoIngreso.pin => _camposPin(),
                    ModoIngreso.correo => _camposCorreo(),
                  },
                  const SizedBox(height: Espacio.xl),
                  FilledButton(
                    onPressed: _puedeEnviar && entorno.configurado ? _enviar : null,
                    child: _entrando
                        ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                        : const Text('Ingresar'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  List<Widget> _camposPin() => [
        TextField(
          controller: _cedula,
          keyboardType: TextInputType.text,
          autocorrect: false,
          enableSuggestions: false,
          textCapitalization: TextCapitalization.characters,
          textInputAction: TextInputAction.next,
          // El servidor normaliza puntos y espacios, así que se acepta como la persona la tenga memorizada.
          decoration: const InputDecoration(labelText: 'Cédula', hintText: '1.234.567'),
          onChanged: (_) => setState(() {}),
        ),
        const SizedBox(height: Espacio.lg),
        TextField(
          controller: _pin,
          obscureText: true,
          keyboardType: TextInputType.number,
          maxLength: largoMaximoPin,
          autocorrect: false,
          enableSuggestions: false,
          inputFormatters: [FilteringTextInputFormatter.digitsOnly],
          decoration: InputDecoration(labelText: 'PIN', errorText: _error, counterText: ''),
          onChanged: (_) => setState(() {}),
          onSubmitted: (_) {
            if (_puedeEnviar) _enviar();
          },
        ),
      ];

  List<Widget> _camposCorreo() => [
        TextField(
          controller: _correo,
          keyboardType: TextInputType.emailAddress,
          autocorrect: false,
          textInputAction: TextInputAction.next,
          autofillHints: const [AutofillHints.username],
          decoration: const InputDecoration(labelText: 'Correo', hintText: 'nombre@boterosoto.com.co'),
          onChanged: (_) => setState(() {}),
        ),
        const SizedBox(height: Espacio.lg),
        TextField(
          controller: _clave,
          obscureText: !_verClave,
          textInputAction: TextInputAction.done,
          autofillHints: const [AutofillHints.password],
          decoration: InputDecoration(
            labelText: 'Contraseña',
            errorText: _error,
            suffixIcon: IconButton(
              tooltip: _verClave ? 'Ocultar contraseña' : 'Mostrar contraseña',
              icon: Icon(_verClave ? Icons.visibility_off_outlined : Icons.visibility_outlined),
              onPressed: () => setState(() => _verClave = !_verClave),
            ),
          ),
          onChanged: (_) => setState(() {}),
          onSubmitted: (_) {
            if (_puedeEnviar) _enviar();
          },
        ),
      ];
}

class _Aviso extends StatelessWidget {
  const _Aviso({required this.texto, required this.color});

  final String texto;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(Espacio.md),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.10),
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: color.withValues(alpha: 0.4)),
        ),
        child: Text(texto, style: Theme.of(context).textTheme.bodyMedium, key: const Key('aviso-login')),
      );
}
