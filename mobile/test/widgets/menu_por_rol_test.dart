import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/app/app.dart';
import 'package:traza_autos/app/sin_permisos_page.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';

/// El menú sale de las capacidades, y la autorización de verdad la impone la API: esto evita ofrecer puertas que
/// el servidor cierra. Es también la primera prueba que ejercita `router.dart`.

Sesion sesionCon(String rol, List<String> capacidades) => Sesion(
      token: 'jwt',
      usuario: Usuario(id: 'u1', nombre: 'Quien Sea', email: '', rol: rol, capacidades: capacidades),
    );

/// Varios fotogramas en vez de pumpAndSettle: la app muestra indicadores que no paran solos. Son bastantes
/// porque hay que dejar terminar la animación de la transición: hasta que acaba, la página que sale sigue en el
/// árbol y `find` la encuentra.
Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 16; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<void> abrir(WidgetTester tester, Sesion? sesion) async {
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes(sesion: sesion).overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
      ],
      child: const TrazaApp(),
    ),
  );
  await avanzar(tester);
}

List<String> etiquetas(WidgetTester tester) => tester
    .widgetList<NavigationDestination>(find.byType(NavigationDestination))
    .map((d) => d.label)
    .toList();

void main() {
  // S6-HU-02.4: lo del conductor es su viaje. No ve la lista de informes ni Sofasa: con una sola pestaña ni siquiera
  // se dibuja la barra.
  testWidgets('el conductor no ve Sofasa ni Inspecciones: le toca su viaje y sus unidades', (tester) async {
    await abrir(tester, sesionCon('conductor', capacidadesDeRol('conductor')));

    expect(find.byType(MisViajesPage), findsOneWidget);
    expect(find.byType(NavigationBar), findsNothing);
    expect(find.text('Sofasa'), findsNothing);
    expect(find.text('Inspecciones'), findsNothing);
  });

  testWidgets('el coordinador de calidad sí la ve', (tester) async {
    await abrir(tester, sesionCon('coordinador_calidad', capacidadesDeRol('coordinador_calidad')));

    expect(etiquetas(tester), ['Mis viajes', 'Inspecciones', 'Sofasa']);
  });

  testWidgets('entra por su primera pestaña', (tester) async {
    await abrir(tester, sesionCon('conductor', capacidadesDeRol('conductor')));

    expect(find.byType(MisViajesPage), findsOneWidget);
  });

  // Que la barra esconda la puerta no basta: la ruta puede abrirse por un enlace o quedar abierta cuando a alguien
  // le cambian el rol con la sesión puesta.
  testWidgets('una ruta sin permiso muestra «sin permisos», no la pantalla', (tester) async {
    await abrir(tester, sesionCon('conductor', capacidadesDeRol('conductor')));

    tester.element(find.byType(MisViajesPage)).go('/sofasa');
    await avanzar(tester);

    expect(find.byType(SinPermisosPage), findsOneWidget);
    expect(find.text('Sin permisos'), findsOneWidget);
  });

  testWidgets('la pantalla de sin permisos dice a dónde sí puede ir', (tester) async {
    await abrir(tester, sesionCon('conductor', capacidadesDeRol('conductor')));
    tester.element(find.byType(MisViajesPage)).go('/sofasa');
    await avanzar(tester);

    expect(find.textContaining('Mis viajes'), findsWidgets);
    expect(find.textContaining('Mis viajes, Inspecciones'), findsNothing, reason: 'al conductor no se le ofrece Inspecciones');
  });

  // Lo que manda el servidor pesa más que el rol: un inspector al que le quitaron registrar_inspecciones ve los
  // informes pero no puede abrir la captura.
  testWidgets('capturar exige más que mirar', (tester) async {
    await abrir(tester, sesionCon('inspector', [Capacidad.verViajes, Capacidad.verInspecciones]));
    tester.element(find.byType(MisViajesPage)).go('/inspecciones/vin/VIN1/captura/e1?proceso=Cargue');
    await avanzar(tester);

    expect(find.byType(SinPermisosPage), findsOneWidget);
  });

  testWidgets('con una sola pestaña no se dibuja la barra', (tester) async {
    await abrir(tester, sesionCon('comercial', [Capacidad.verViajes]));

    expect(find.byType(NavigationBar), findsNothing);
    expect(find.byType(MisViajesPage), findsOneWidget);
  });
}
