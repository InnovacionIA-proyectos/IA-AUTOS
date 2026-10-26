import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/tema/tema.dart';
import '../features/auth/presentation/auth_controller.dart';
import 'pestanas.dart';

/// Lo que se ve al abrir algo que el rol no alcanza.
///
/// Se dibuja en el lugar de la pantalla, dentro del shell, para que la barra de abajo siga ahí y la persona pueda
/// irse a lo suyo sin cerrar sesión. La autorización de verdad la impone la API; esto solo evita el 403 y explica.
class SinPermisosPage extends ConsumerWidget {
  const SinPermisosPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final usuario = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario));
    final otras = pestanasDe(usuario).map((p) => p.etiqueta).join(', ');

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(Espacio.xl),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.lock_outline, size: 56, color: Colores.textoSecundario),
                const SizedBox(height: Espacio.lg),
                Text('Sin permisos', style: tema.titleLarge, textAlign: TextAlign.center),
                const SizedBox(height: Espacio.sm),
                Text(
                  'Tu usuario no tiene acceso a esta pantalla.',
                  style: tema.bodyMedium,
                  textAlign: TextAlign.center,
                ),
                if (otras.isNotEmpty) ...[
                  const SizedBox(height: Espacio.sm),
                  Text('Puedes entrar a: $otras.', style: tema.bodySmall, textAlign: TextAlign.center),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Deja pasar a [child] solo si la persona tiene [capacidad]; si no, muestra [SinPermisosPage] en su lugar.
class Permitido extends ConsumerWidget {
  const Permitido({super.key, required this.capacidad, required this.child});

  final String capacidad;
  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final puede = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.puede(capacidad) ?? false));
    return puede ? child : const SinPermisosPage();
  }
}
