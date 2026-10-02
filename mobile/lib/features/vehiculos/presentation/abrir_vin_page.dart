import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../../core/tema/tema.dart';

const largoVin = 17;

class AbrirVinPage extends StatefulWidget {
  const AbrirVinPage({super.key});

  @override
  State<AbrirVinPage> createState() => _EstadoAbrirVinPage();
}

class _EstadoAbrirVinPage extends State<AbrirVinPage> {
  final _vin = TextEditingController();

  @override
  void dispose() {
    _vin.dispose();
    super.dispose();
  }

  bool get _valido => _vin.text.trim().length == largoVin;

  void _abrir() => context.push('/inspecciones/vin/${Uri.encodeComponent(_vin.text.trim().toUpperCase())}');

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Scaffold(
      appBar: AppBar(title: const Text('Nueva inspección')),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(Espacio.lg),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                'Digita el VIN del vehículo para ver su último informe e iniciar el cambio de responsabilidad.',
                style: tema.bodyMedium?.copyWith(color: Colores.textoSecundario),
              ),
              const SizedBox(height: Espacio.lg),
              TextField(
                controller: _vin,
                autofocus: true,
                autocorrect: false,
                enableSuggestions: false,
                textCapitalization: TextCapitalization.characters,
                maxLength: largoVin,
                style: const TextStyle(fontFamily: 'monospace', fontWeight: FontWeight.w600),
                inputFormatters: [FilteringTextInputFormatter.allow(RegExp('[A-Za-z0-9]')), _MayusculasFormatter()],
                decoration: const InputDecoration(labelText: 'VIN (17 caracteres)'),
                onChanged: (_) => setState(() {}),
                onSubmitted: (_) {
                  if (_valido) _abrir();
                },
              ),
              const Spacer(),
              FilledButton(onPressed: _valido ? _abrir : null, child: const Text('Abrir VIN')),
            ],
          ),
        ),
      ),
    );
  }
}

class _MayusculasFormatter extends TextInputFormatter {
  @override
  TextEditingValue formatEditUpdate(TextEditingValue anterior, TextEditingValue nuevo) =>
      nuevo.copyWith(text: nuevo.text.toUpperCase());
}
