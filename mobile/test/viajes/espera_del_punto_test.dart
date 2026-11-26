import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';
import 'package:traza_autos/features/viajes/domain/espera_del_punto.dart';
import 'package:traza_autos/features/viajes/presentation/seccion_pendientes.dart';

import '../support/dobles_viajes.dart';

/// S8-HU-05.2: el reloj de la espera del inspector del punto, a quién se le ofrece «No hay inspector» y cuándo el
/// conductor puede revisar la llegada sin él.

/// Las 08:00 de Colombia.
final activada = DateTime.utc(2026, 11, 26, 13);

DateTime a(int minutos, [int segundos = 0]) => activada.add(Duration(minutes: minutos, seconds: segundos));

EsperaDelPunto esperaDe({
  EstadoEspera estado = EstadoEspera.esperando,
  DateTime? avisoAt,
  bool planBHabilitado = false,
  String? inspector,
}) =>
    EsperaDelPunto(
      estado: estado,
      activadaAt: activada,
      avisoDesde: a(15),
      avisoAt: avisoAt,
      planBDesde: a(45),
      planBHabilitado: planBHabilitado,
      inspectorNombre: inspector,
    );

SinInspectorParadaJob activacionDe(DateTime momento, {bool fallido = false}) => SinInspectorParadaJob(
      id: 'a1',
      etiqueta: 'No hay inspector',
      creadoEn: 1,
      usuarioId: 'u9',
      viajeId: 'v1',
      lugarId: 'l-Puerto',
      motivo: 'inspector_no_llego',
      fechaMomento: isoConZona(momento),
      avisoMinutos: 15,
      planBMinutos: 30,
      fallido: fallido,
    );

EntregaPunto sinNadie({EsperaDelPunto? espera, bool conductorLlego = true}) =>
    puntoDe('Puerto', receptor: ReceptorParada.sinReceptor, espera: espera, conductorLlego: conductorLlego);

void main() {
  group('lo que trae la parada', () {
    test('la espera, X e Y, los daños por calificar y la marca del plan B se leen del contrato', () {
      final punto = EntregaPunto.fromJson({
        'viaje_id': 'v1',
        'lugar_id': 'l1',
        'lugar': 'Puerto de Cartagena',
        'lugar_tiene_inspector': true,
        'receptor': 'sin_receptor',
        'espera': {
          'id': 'esp-1',
          'estado': 'esperando',
          'motivo_sin_inspector': 'inspector_no_llego',
          'motivo_sin_inspector_texto': null,
          'activada_at': '2026-11-26T13:00:00Z',
          'aviso_desde': '2026-11-26T13:20:00Z',
          'aviso_at': '2026-11-26T13:20:05Z',
          'plan_b_desde': '2026-11-26T14:00:00Z',
          'plan_b_habilitado': false,
          'inspector_nombre': null,
          'inspector_llego_at': null,
          'cerrada_at': null,
        },
        'espera_aviso_minutos': 20,
        'espera_plan_b_minutos': 40,
        'danos_por_calificar': [
          {'hallazgo_id': 'h1', 'vin': 'VIN1', 'parte': 'Puerta delantera', 'clase_dano': 'golpe', 'evidencia_id': 'ev1'},
        ],
        'unidades': [
          {'vin': 'VIN1', 'estado': 'cerrada', 'entregada_sin_inspector_del_punto': true},
        ],
      });

      final espera = punto.espera!;
      expect((espera.estado, espera.abierta, espera.motivo), (EstadoEspera.esperando, true, 'inspector_no_llego'));
      expect((espera.activadaAt, espera.avisoDesde, espera.planBDesde), (activada, a(20), a(60)));
      expect(espera.avisoAt, a(20, 5));
      expect((punto.esperaAvisoMinutos, punto.esperaPlanBMinutos), (20, 40));
      final dano = punto.danosPorCalificar.single;
      expect((dano.hallazgoId, dano.vin, dano.parte, dano.clase, dano.evidenciaId), ('h1', 'VIN1', 'Puerta delantera', 'golpe', 'ev1'));
      expect(punto.unidades.single.sinInspectorDelPunto, isTrue);
    });

    test('un servidor sin espera deja los plazos del contrato y ninguna espera', () {
      final punto = EntregaPunto.fromJson({'viaje_id': 'v1', 'lugar_id': 'l1', 'lugar': 'Norte', 'unidades': <dynamic>[]});

      expect(punto.espera, isNull);
      expect((punto.esperaAvisoMinutos, punto.esperaPlanBMinutos), (15, 30));
      expect(punto.danosPorCalificar, isEmpty);
    });

    test('los estados de la espera', () {
      expect(EstadoEspera.desde('inspector_llego'), EstadoEspera.inspectorLlego);
      expect(EstadoEspera.desde('cerrada_sin_inspector_del_punto'), EstadoEspera.cerradaSinInspectorDelPunto);
      expect(EstadoEspera.desde('anulada'), EstadoEspera.anulada);
    });
  });

  // CA-3: lo que dice el reloj, contado con la hora de activación guardada y los minutos X e Y.
  group('el reloj', () {
    final reloj = RelojEspera.local(activadaAt: activada, avisoMinutos: 15, planBMinutos: 30);

    test('antes del aviso cuenta los minutos hacia arriba', () {
      expect(reloj.textoAviso(a(3, 30)), 'Avisaremos al coordinador en 12 min');
      expect(reloj.textoPlanB(a(3, 30)), 'Podrás cerrar sin inspector del punto en 42 min');
      expect(reloj.textoAviso(a(14, 59)), 'Avisaremos al coordinador en 1 min', reason: 'nunca «en 0 min»');
    });

    test('pasado X el coordinador ya fue avisado; pasado X + Y ya se puede cerrar', () {
      expect(reloj.textoAviso(a(27)), 'El coordinador ya fue avisado');
      expect(reloj.textoPlanB(a(27)), 'Podrás cerrar sin inspector del punto en 18 min');
      expect(reloj.planBHabilitado(a(44, 59)), isFalse);
      expect(reloj.planBHabilitado(a(45)), isTrue);
      expect(reloj.textoPlanB(a(45)), 'Ya puedes cerrar sin inspector del punto');
    });

    // ESC-09: una nueva activación cuenta desde cero.
    test('una activación nueva cuenta desde su propia hora', () {
      final otra = RelojEspera.local(activadaAt: a(55), avisoMinutos: 15, planBMinutos: 30);

      expect(otra.avisoDesde, a(70));
      expect(otra.planBDesde, a(100));
    });

    // CA-3: con la espera del servidor, «ya fue avisado» lo dice el servidor, no la hora del teléfono.
    test('con la espera del servidor y sin su aviso, no se da por avisado', () {
      final delServidor = RelojEspera.delServidor(esperaDe());

      expect(delServidor.avisado(a(27)), isFalse);
      expect(delServidor.avisoPorConfirmar(a(27)), isTrue);
      expect(delServidor.textoAviso(a(27)), 'Avisaremos al coordinador en un momento');
      expect(delServidor.avisoPorConfirmar(a(10)), isFalse);
    });

    test('la espera anotada en este teléfono cuenta con el reloj del teléfono', () {
      final anotada = EsperaDelPunto.fromJson(EsperaDelPunto.anotada(activacionDe(activada)).toJson());
      final reloj = RelojEspera.delServidor(anotada);

      expect((anotada.deEsteTelefono, anotada.abierta, anotada.planBDesde), (true, true, a(45)));
      expect((reloj.local, reloj.sinEnviar), (true, false));
      expect(reloj.textoAviso(a(27)), 'El coordinador ya fue avisado');
    });

    test('con señal manda el servidor: su aviso y su plan B, aunque el teléfono vaya atrasado', () {
      final delServidor = RelojEspera.delServidor(esperaDe(avisoAt: a(15), planBHabilitado: true));

      expect(delServidor.textoAviso(a(1)), 'El coordinador ya fue avisado');
      expect(delServidor.textoPlanB(a(1)), 'Ya puedes cerrar sin inspector del punto');
    });
  });

  group('la espera que vale en la parada', () {
    test('la del servidor, si está abierta', () {
      final reloj = relojDeLaParada(sinNadie(espera: esperaDe()), activacion: activacionDe(a(30)));

      expect((reloj?.activadaAt, reloj?.local), (activada, false));
    });

    test('si no, la activación que espera señal, con los X e Y que trajo la parada', () {
      final reloj = relojDeLaParada(sinNadie(), activacion: activacionDe(activada));

      expect((reloj?.activadaAt, reloj?.planBDesde, reloj?.local), (activada, a(45), true));
    });

    // CA-5 y ESC-07: el reloj nunca le quita la entrega al inspector.
    test('ninguna si un inspector tomó la entrega, aunque haya pasado el plazo', () {
      final tomada = puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: 'Pedro', espera: esperaDe(planBHabilitado: true));

      expect(relojDeLaParada(tomada, activacion: activacionDe(activada)), isNull);
    });

    test('ninguna si la espera terminó, si la activación falló o si no hay nada', () {
      expect(relojDeLaParada(sinNadie(espera: esperaDe(estado: EstadoEspera.inspectorLlego))), isNull);
      expect(relojDeLaParada(sinNadie(), activacion: activacionDe(activada, fallido: true)), isNull);
      expect(relojDeLaParada(sinNadie()), isNull);
    });
  });

  // CA-1 y ESC-02: a quién y dónde se ofrece «No hay inspector».
  group('«No hay inspector»', () {
    test('al conductor, en la parada con inspectores, después de su «Llegué» y sin nadie que la tome', () {
      expect(ofreceNoHayInspector(sinNadie(), rol: 'conductor'), isTrue);
    });

    test('nunca en una parada sin inspectores, con la entrega tomada, antes del «Llegué» ni con la espera corriendo', () {
      expect(ofreceNoHayInspector(puntoDe('Norte', tieneInspector: false, receptor: ReceptorParada.personaDelLugar), rol: 'conductor'), isFalse);
      expect(ofreceNoHayInspector(puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: 'Pedro'), rol: 'conductor'), isFalse);
      expect(ofreceNoHayInspector(sinNadie(conductorLlego: false), rol: 'conductor'), isFalse);
      expect(
        ofreceNoHayInspector(sinNadie(), rol: 'conductor', reloj: RelojEspera.delServidor(esperaDe())),
        isFalse,
      );
    });

    test('nunca a inspector, coordinador, admin ni super admin', () {
      for (final rol in ['inspector', 'coordinador_calidad', 'admin', 'super_admin']) {
        expect(ofreceNoHayInspector(sinNadie(), rol: rol), isFalse, reason: rol);
      }
    });

    // CA-5 y ESC-09: si el inspector la soltó, vuelve.
    test('vuelve si el inspector que llegó soltó la entrega', () {
      final soltada = sinNadie(espera: esperaDe(estado: EstadoEspera.inspectorLlego, inspector: 'Pedro'));

      expect(ofreceNoHayInspector(soltada, rol: 'conductor', reloj: relojDeLaParada(soltada)), isTrue);
    });
  });

  // CA-9 (C10): la revisión de llegada del conductor en una parada con inspectores espera al plan B.
  group('la revisión de llegada del conductor', () {
    test('en una parada sin inspectores, o sin saber cómo está, no cambia nada', () {
      expect(bloqueoDeLaLlegadaDelConductor(puntoDe('Norte', tieneInspector: false), ahora: a(0)), isNull);
      expect(bloqueoDeLaLlegadaDelConductor(null, ahora: a(0)), isNull);
    });

    test('sin espera la hace el inspector del punto; con espera dice cuánto falta; con el plan B la captura él', () {
      expect(bloqueoDeLaLlegadaDelConductor(sinNadie(), ahora: a(0)), 'La revisión de llegada la hace el inspector del punto');
      expect(
        bloqueoDeLaLlegadaDelConductor(sinNadie(espera: esperaDe()), ahora: a(40)),
        'La revisión de llegada la hace el inspector del punto: podrás hacerla sin él en 5 min',
      );
      expect(bloqueoDeLaLlegadaDelConductor(sinNadie(espera: esperaDe()), ahora: a(46)), isNull);
      expect(bloqueoDeLaLlegadaDelConductor(sinNadie(), activacion: activacionDe(activada), ahora: a(45)), isNull);
    });

    test('con la entrega tomada por un inspector, la hace él', () {
      final tomada = puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: 'Pedro');

      expect(bloqueoDeLaLlegadaDelConductor(tomada, ahora: a(90)), 'La revisión de llegada la hace el inspector del punto');
    });
  });

  // CA-7 (ESC-08): el inspector que llega tarde a una parada cerrada con el plan B.
  group('el reclamo de una parada cerrada sin inspector del punto', () {
    test('dice la hora del cierre en Colombia y que es definitivo', () {
      const error = ErrorApi(
        message: 'texto del servidor',
        code: 'CONFLICT',
        status: 409,
        details: {'motivo': 'entrega_cerrada_sin_inspector', 'cerrada_at': '2026-11-26T14:20:00Z'},
      );

      expect(mensajeDelReclamo(error), 'Esta entrega se cerró sin inspector del punto a las 09:20: el cierre es definitivo');
    });

    test('los demás rechazos se dicen como los dice el servidor', () {
      const error = ErrorApi(message: 'Ya fue tomado por otro inspector', code: 'CONFLICT', status: 409, details: {'motivo': 'entrega_tomada'});

      expect(mensajeDelReclamo(error), 'Ya fue tomado por otro inspector');
    });
  });
}
