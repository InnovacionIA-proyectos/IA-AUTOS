/// La novedad del **viaje** (S8-HU-07.3): lo que el conductor o un inspector que participa reporta sobre el viaje y
/// no sobre un carro. No es un hallazgo: no cuenta en las novedades ni en los daños de ningún carro.
library;

import '../../catalogos/domain/catalogos.dart';
import 'pendiente.dart';
import 'viaje.dart';

/// Lo que máximo admite el servidor en el texto: con más responde 400 «Escribe máximo 300 caracteres».
const novedadViajeTextoMax = 300;

/// Quién la reporta. El coordinador, el administrador y el super administrador reciben 403: a ellos no se les ofrece.
bool reportaNovedadViaje(String? rol) => rol == 'conductor' || rol == 'inspector';

/// Valor de `rol` de un tipo que eligen los dos.
const rolNovedadViajeTodos = 'todos';

/// Los tipos que puede elegir [rol]: los activos de `tipos-novedad-viaje` cuyo `rol` es el suyo o `todos`, en su
/// orden (S8-HU-07.3 CA-2). Un tipo del otro rol el servidor lo rechaza con 400 «Ese tipo de novedad no es para tu
/// rol»: ofrecerlo sería mandar a escribir la novedad para perderla. Un tipo sin `rol` es de los dos, que es lo que
/// el servidor pone por defecto.
List<ValorCatalogo> tiposNovedadViajePara(Catalogos catalogos, String? rol) => [
      for (final tipo in catalogos.seleccionables(Catalogo.tiposNovedadViaje))
        if (tipo.campo<String>('rol') case final deQuien when deQuien == null || deQuien == rolNovedadViajeTodos || deQuien == rol)
          tipo,
    ];

/// Un proceso del viaje en el que participa el inspector que mira, con lo que la novedad tiene que mandar.
typedef ReclamoNovedad = ({String proceso, String? lugarId, String? lugar});

/// En qué procesos del viaje puede reportar el inspector (S8-HU-07.3 CA-1), con las mismas reglas del servidor: en el
/// cargue, si es el inspector del viaje o tomó el cargue; en la entrega, cada parada que tomó, con su `lugar_id`. A
/// cualquier otro el servidor le responde 403 `inspector_sin_ese_proceso`, así que no se le ofrece.
List<ReclamoNovedad> reclamosDeNovedadViaje(Viaje viaje, {required String? yoId, required List<Pendiente> tomados}) => [
      if ((yoId != null && viaje.inspectorId == yoId) || tomados.any((t) => !t.esEntrega))
        (proceso: procesoCargue, lugarId: null, lugar: null),
      for (final tomado in tomados)
        if (tomado.esEntrega && tomado.lugarId != null) (proceso: procesoEntrega, lugarId: tomado.lugarId, lugar: tomado.lugar),
    ];

/// Lo que dice el botón: con un solo reclamo, «Novedad del viaje»; con varios, de cuál es cada uno.
String textoBotonNovedadViaje(ReclamoNovedad reclamo, {required bool varios}) => switch (reclamo) {
      _ when !varios => 'Novedad del viaje',
      (proceso: procesoEntrega, lugar: final lugar?, lugarId: _) when lugar.isNotEmpty => 'Novedad del viaje · $lugar',
      (proceso: procesoEntrega, lugar: _, lugarId: _) => 'Novedad del viaje · Entrega',
      _ => 'Novedad del viaje · Cargue',
    };
