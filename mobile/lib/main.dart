import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'app/app.dart';
import 'app/proveedores.dart';
import 'core/config/entorno.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting('es_CO');

  runApp(
    ProviderScope(
      // Un fallo de red no debe reintentarse solo en bucle: la cola ya gestiona sus propios reintentos.
      retry: (_, _) => null,
      overrides: [entornoProvider.overrideWithValue(Entorno.desdeCompilacion())],
      child: const TrazaApp(),
    ),
  );
}
