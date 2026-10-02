import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/tema/tema.dart';
import '../features/sync/presentation/sync_controller.dart';
import 'router.dart';

class TrazaApp extends ConsumerWidget {
  const TrazaApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // Mantiene viva la sincronización de la cola durante toda la sesión de la app.
    ref.watch(syncControllerProvider);

    return MaterialApp.router(
      title: 'Traza Autos 360°',
      debugShowCheckedModeBanner: false,
      theme: crearTema(),
      themeMode: ThemeMode.light,
      routerConfig: ref.watch(routerProvider),
      locale: const Locale('es', 'CO'),
      supportedLocales: const [Locale('es', 'CO'), Locale('es')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
    );
  }
}
