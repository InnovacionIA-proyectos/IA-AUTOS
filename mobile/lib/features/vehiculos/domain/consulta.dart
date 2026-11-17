/// Quién consulta carros que no están en sus viajes, y qué se le ofrece cuando abre uno.
///
/// La línea es la misma que divide al servidor: el inspector y el conductor van recortados a sus viajes
/// (`SoloSusViajes()`), y a los demás —el coordinador de calidad, el comercial, los administradores— el servidor
/// les responde con los carros de su cliente. Por eso su búsqueda no puede salir de la copia local de viajes: ahí
/// no hay nada que buscar cuando no se tiene ninguno, que es justo el caso del coordinador.
library;

import '../../auth/domain/sesion.dart';

/// ¿Esta persona busca carros en el servidor, en vez de entre las unidades de sus viajes?
bool buscaEnElServidor(String? rol) => rol != null && !soloSusViajes(rol);

/// Lo que se dice en la ficha a quien consulta y no trabaja el carro, en vez de ofrecerle botones que el servidor
/// le va a rechazar.
const mensajeSoloConsulta = 'Solo consulta: esta ficha se mira, no se trabaja.';

/// Por qué no se pudo abrir un carro. A quien va por sus viajes le falta el viaje; a quien consulta los de su
/// cliente le falta el cliente, y mandarlo a buscar un viaje sería mandarlo a buscar algo que no existe.
String mensajeSinAcceso(String? rol) =>
    buscaEnElServidor(rol) ? 'Este carro no es de tu cliente.' : 'Esta unidad no está en tus viajes.';

String detalleSinAcceso(String? rol) => buscaEnElServidor(rol)
    ? 'Solo se abren los carros del cliente al que perteneces.'
    : 'Solo se abren las unidades de los viajes en los que estás.';
