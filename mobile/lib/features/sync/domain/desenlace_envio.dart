import 'job.dart';

/// Cómo quedó **un** envío concreto.
///
/// La pantalla que originó un envío tiene que hablar de **ese** envío, no del total de la cola: con dos cosas
/// pendientes de otra unidad, mirar el contador hacía que una entrega aceptada se anunciara como «quedó en cola»,
/// y que un informe recién rechazado se anunciara como enviado. La captura de informe ya lo hacía bien
/// (`captura_page.dart`); esto es ese mismo criterio, escrito una vez para todas las pantallas.
/// Por qué una pasada de la cola no llegó a tocar ningún envío.
///
/// Se distingue a propósito: «sin señal» y «el servidor no responde» se arreglan de maneras distintas, y quien
/// está en el patio necesita saber cuál de las dos le está pasando. Culpar a la señal con el wifi perfecto manda
/// a buscar el problema donde no está.
enum MotivoPausa { sinSenal, apiCaida }

enum FaseEnvio {
  /// Salió: el servidor lo aceptó y el job ya no está en la cola.
  registrado,

  /// Guardado y todavía sin intentar: no había señal estable o la API no respondía.
  enCola,

  /// Se intentó, falló por algo pasajero y se volverá a intentar solo.
  enEspera,

  /// El servidor lo rechazó por algo que no se arregla reintentando: queda esperando a una persona.
  rechazado,
}

/// El desenlace de [jobId] con el motivo y, si se va a reintentar solo, cuánto falta.
class DesenlaceEnvio {
  const DesenlaceEnvio(this.fase, {this.motivo, this.espera});

  final FaseEnvio fase;

  /// Lo que dijo el servidor. Null mientras no se haya intentado.
  final String? motivo;

  /// Cuánto falta para el reintento automático. Null si no lo habrá (ya salió, o lo rechazaron).
  final Duration? espera;

  bool get salio => fase == FaseEnvio.registrado;
}

/// Busca [jobId] en [cola] y dice cómo quedó. Que no esté es la buena noticia: el procesador borra el job
/// cuando el servidor lo acepta.
///
/// [pausa] es por qué la pasada no tocó la cola, cuando no la tocó. Hace falta porque en ese caso el job queda
/// **sin** motivo propio —nadie lo intentó—, y sin esto un servidor caído se anunciaba como «se enviará cuando la
/// señal sea estable» aunque la señal estuviera perfecta.
DesenlaceEnvio desenlaceDe(Iterable<Job> cola, String jobId, {MotivoPausa? pausa}) {
  final job = cola.where((j) => j.id == jobId).firstOrNull;
  if (job == null) return const DesenlaceEnvio(FaseEnvio.registrado);
  if (job.fallido) return DesenlaceEnvio(FaseEnvio.rechazado, motivo: job.error);
  if (job.error == null) {
    // El servidor caído sí se reintenta solo, y el tiempo se puede prometer: la cola ya lo tiene programado.
    return pausa == MotivoPausa.apiCaida
        ? DesenlaceEnvio(
            FaseEnvio.enEspera,
            motivo: 'el servidor no responde',
            espera: esperaTrasIntentos(job.intentos),
          )
        : const DesenlaceEnvio(FaseEnvio.enCola);
  }
  return DesenlaceEnvio(FaseEnvio.enEspera, motivo: job.error, espera: esperaTrasIntentos(job.intentos));
}

/// Cuánto espera la cola antes de volver a intentar, según los intentos que lleve.
///
/// Crece y se detiene a los diez minutos: con la red conectada y el servidor sin responder, el primer reintento
/// llega en quince segundos y el peor caso sigue siendo una vez cada diez minutos. Es lo que permite prometer un
/// tiempo concreto en pantalla sin dejar el teléfono despertándose toda la jornada: en ocho horas de patio son
/// unos cincuenta intentos, no miles.
Duration esperaTrasIntentos(int intentos) {
  const escala = [15, 30, 60, 120, 300, 600];
  return Duration(seconds: escala[intentos.clamp(0, escala.length - 1)]);
}

/// Cómo se dice una espera en una frase: «ahora mismo», «en 1 segundo», «en 15 segundos», «en 2 minutos».
String enPalabras(Duration espera) {
  final segundos = espera.inSeconds;
  // Un reloj que ya venció se dice en presente: «se reintenta en 0 segundos» se lee como un error.
  if (segundos <= 0) return 'ahora mismo';
  if (segundos < 60) return segundos == 1 ? 'en 1 segundo' : 'en $segundos segundos';
  final minutos = (segundos / 60).round();
  return minutos == 1 ? 'en 1 minuto' : 'en $minutos minutos';
}
