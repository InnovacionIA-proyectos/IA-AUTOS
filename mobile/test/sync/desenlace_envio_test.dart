import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/sync/domain/desenlace_envio.dart';
import 'package:traza_autos/features/inspecciones/domain/nota_aclaratoria.dart';
import 'package:traza_autos/features/sync/domain/job.dart';

/// Qué le pasó a **un** envío. La pantalla que lo originó tiene que hablar de ese envío y no del total de la
/// cola: quien acaba de firmar una entrega no quiere saber que otra unidad tiene algo pendiente.
NotaJob job({String id = 'j1', int intentos = 0, String? error, bool fallido = false}) => NotaJob(
      id: id,
      etiqueta: 'Nota',
      creadoEn: 1,
      usuarioId: 'u1',
      entidad: EntidadNota.inspeccion,
      entidadId: 'i1',
      texto: 'texto',
      intentos: intentos,
      error: error,
      fallido: fallido,
    );

void main() {
  group('desenlaceDe', () {
    // El procesador borra el job cuando el servidor lo acepta: que no esté es la buena noticia.
    test('si ya no está en la cola, salió', () {
      expect(desenlaceDe([job(id: 'otro')], 'j1').fase, FaseEnvio.registrado);
      expect(desenlaceDe(const [], 'j1').salio, isTrue);
    });

    // Lo que se veía en el patio: con dos pendientes de otra unidad, una entrega aceptada se anunciaba como
    // «quedó en cola».
    test('lo pendiente de otro envío no cambia el desenlace del mío', () {
      final cola = [job(id: 'otro-1'), job(id: 'otro-2', fallido: true, error: 'El viaje está finalizado')];

      expect(desenlaceDe(cola, 'j1').fase, FaseEnvio.registrado);
    });

    test('guardado y sin intentar es cola, no espera: todavía no falló nada', () {
      final d = desenlaceDe([job()], 'j1');

      expect((d.fase, d.motivo, d.espera), (FaseEnvio.enCola, null, null));
    });

    test('rechazado dice el motivo y no promete un reintento que no va a ocurrir', () {
      final d = desenlaceDe([job(fallido: true, error: 'El viaje está finalizado')], 'j1');

      expect(d.fase, FaseEnvio.rechazado);
      expect(d.motivo, 'El viaje está finalizado');
      expect(d.espera, isNull, reason: 'un fallido espera a una persona, no al reloj');
    });

    test('lo que falló por algo pasajero dice el motivo y cuándo se vuelve a intentar', () {
      final d = desenlaceDe([job(intentos: 1, error: 'El servidor no responde')], 'j1');

      expect(d.fase, FaseEnvio.enEspera);
      expect(d.motivo, 'El servidor no responde');
      expect(d.espera, esperaTrasIntentos(1));
    });

    // Con el servidor caído la cola ni siquiera arranca, así que el job queda sin motivo propio. Sin saber por
    // qué se pausó la pasada, el aviso culpaba a la señal con el wifi perfecto.
    test('con el servidor caído se dice eso, no que falte señal', () {
      final d = desenlaceDe([job()], 'j1', pausa: MotivoPausa.apiCaida);

      expect(d.fase, FaseEnvio.enEspera);
      expect(d.motivo, 'el servidor no responde');
      expect(d.espera, esperaTrasIntentos(0), reason: 'la cola ya lo tiene programado: se puede prometer');
    });

    test('sin señal sí es cola: no hay nada que prometer hasta que vuelva', () {
      final d = desenlaceDe([job()], 'j1', pausa: MotivoPausa.sinSenal);

      expect((d.fase, d.espera), (FaseEnvio.enCola, null));
    });

    // El motivo del propio job manda: ya se intentó y el servidor dijo algo concreto, que es más útil que
    // «no responde».
    test('si el job ya trae su motivo, ese gana sobre el de la pausa', () {
      final d = desenlaceDe([job(intentos: 2, error: 'Faltan fotos obligatorias')], 'j1', pausa: MotivoPausa.apiCaida);

      expect(d.motivo, 'Faltan fotos obligatorias');
      expect(d.espera, esperaTrasIntentos(2));
    });

    test('un rechazo sigue siendo rechazo aunque la pasada se haya pausado', () {
      final d = desenlaceDe([job(fallido: true, error: 'El viaje está finalizado')], 'j1', pausa: MotivoPausa.apiCaida);

      expect(d.fase, FaseEnvio.rechazado);
      expect(d.espera, isNull);
    });
  });

  group('esperaTrasIntentos', () {
    test('el primer reintento llega pronto: un servidor que parpadea no cuesta una jornada', () {
      expect(esperaTrasIntentos(0), const Duration(seconds: 15));
    });

    test('crece con cada intento', () {
      expect(esperaTrasIntentos(1) > esperaTrasIntentos(0), isTrue);
      expect(esperaTrasIntentos(3) > esperaTrasIntentos(1), isTrue);
    });

    // Sin tope, ocho horas de patio con el servidor caído serían miles de despertares del teléfono.
    test('se detiene a los diez minutos y no sigue creciendo', () {
      expect(esperaTrasIntentos(5), const Duration(minutes: 10));
      expect(esperaTrasIntentos(8), const Duration(minutes: 10));
      expect(esperaTrasIntentos(99), const Duration(minutes: 10));
    });
  });

  group('enPalabras', () {
    test('los segundos se dicen en segundos y los minutos en minutos', () {
      expect(enPalabras(const Duration(seconds: 15)), 'en 15 segundos');
      expect(enPalabras(const Duration(minutes: 1)), 'en 1 minuto');
      expect(enPalabras(const Duration(minutes: 10)), 'en 10 minutos');
    });

    test('uno va en singular: «en 1 segundos» delata que nadie leyó la frase', () {
      expect(enPalabras(const Duration(seconds: 1)), 'en 1 segundo');
    });

    // El reloj de la barra se dibuja con lo que falta, y puede alcanzar el cero antes de que la pasada arranque.
    test('un reloj vencido se dice en presente, no «en 0 segundos»', () {
      expect(enPalabras(Duration.zero), 'ahora mismo');
      expect(enPalabras(const Duration(seconds: -5)), 'ahora mismo');
    });
  });
}
