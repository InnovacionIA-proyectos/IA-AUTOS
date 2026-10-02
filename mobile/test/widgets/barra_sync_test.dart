import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/sync/presentation/barra_sync.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';

class SyncFalso extends SyncController {
  SyncFalso(this.inicial);

  final EstadoSync inicial;
  int sincronizaciones = 0;

  @override
  EstadoSync build() => inicial;

  @override
  Future<void> sincronizar() async => sincronizaciones++;
}

Future<SyncFalso> montar(WidgetTester tester, EstadoSync estado) async {
  final falso = SyncFalso(estado);
  final router = GoRouter(routes: [
    GoRoute(path: '/', builder: (_, _) => const Scaffold(body: BarraSync())),
    GoRoute(path: '/inspecciones/cola', builder: (_, _) => const Scaffold(body: Text('PANTALLA COLA'))),
  ]);
  await tester.pumpWidget(
    ProviderScope(
      overrides: [syncControllerProvider.overrideWith(() => falso)],
      child: MaterialApp.router(theme: crearTema(), routerConfig: router),
    ),
  );
  return falso;
}

void main() {
  testWidgets('todo enviado', (tester) async {
    await montar(tester, const EstadoSync());
    expect(find.text('Todo sincronizado'), findsOneWidget);
    expect(find.text('Sincronizar'), findsNothing);
  });

  testWidgets('con señal y elementos en cola ofrece sincronizar y lo hace al tocar', (tester) async {
    final falso = await montar(tester, const EstadoSync(pendientes: 3));
    expect(find.text('3 en cola'), findsOneWidget);

    await tester.tap(find.text('Sincronizar'));
    await tester.pump();

    expect(falso.sincronizaciones, 1);
  });

  testWidgets('sincronizando muestra el progreso en lugar del botón', (tester) async {
    await montar(tester, const EstadoSync(pendientes: 2, sincronizando: true));
    expect(find.text('2 en cola · sincronizando…'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('Sincronizar'), findsNothing);
  });

  testWidgets('sin señal lo avisa y no ofrece sincronizar', (tester) async {
    await montar(tester, const EstadoSync(online: false, pendientes: 4));
    expect(find.text('Sin señal estable · 4 en cola'), findsOneWidget);
    expect(find.text('Sincronizar'), findsNothing);
  });

  testWidgets('los envíos con error tienen prioridad y llevan a la lista al tocar', (tester) async {
    await montar(tester, const EstadoSync(pendientes: 1, fallidos: 2));
    expect(find.text('2 con error · toca para revisar'), findsOneWidget);

    await tester.tap(find.text('2 con error · toca para revisar'));
    await tester.pumpAndSettle();

    expect(find.text('PANTALLA COLA'), findsOneWidget);
  });
}
