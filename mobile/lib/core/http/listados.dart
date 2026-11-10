import '../errores/error_api.dart';

/// Código del error que levanta la app cuando un listado de la API no viene ni como arreglo ni como página.
const codigoFormaInesperada = 'FORMA_INESPERADA';

/// Los elementos de un listado de la API, venga como arreglo pelado o envuelto en una página
/// `{items, total, pagina, tamano}`.
///
/// Las dos formas conviven en el contrato y **no se pueden deducir de la ruta**: `GET /api/usuarios` responde una
/// página a quien administra usuarios y un arreglo a los demás (`clsUsuariosController.Listar`), así que la forma
/// depende de quién pregunta, no de qué se pide. Otras rutas se paginaron con el tiempo: `/api/inspecciones` y
/// `/api/sofasa/ordenes` ya responden página, mientras `/api/viajes/mios` y `/api/viajes/pendientes` siguen siendo
/// arreglos. Y un celular puede quedar contra una API más vieja o más nueva que él.
///
/// Por qué importa leer las dos: un `as List<dynamic>` sobre una página lanza un `TypeError`, y como los
/// repositorios atrapan cualquier error para caer a la copia local, la pantalla termina diciendo «revisa tu
/// conexión» con la API respondiendo 200. El síntoma culpa a la red de un problema de lectura.
///
/// Lo que **no** se hace es devolver una lista vacía ante una respuesta que no es ninguna de las dos: eso diría
/// «aún no tienes inspecciones» cuando lo que pasó es que no se entendió la respuesta. Se levanta un [ErrorApi]
/// con `status` 200 —el servidor sí respondió, y bien— para que la pantalla muestre su error con el botón de
/// reintentar y para que no se cuente como transitorio: reintentar no cambia la forma de una respuesta.
List<dynamic> elementosDe(Object? respuesta, {required String ruta}) => switch (respuesta) {
      final List<dynamic> lista => lista,
      {'items': final List<dynamic> items} => items,
      _ => throw ErrorApi(
          message: 'No pudimos entender la respuesta del servidor.',
          code: codigoFormaInesperada,
          status: 200,
          details: ['Ruta: $ruta'],
        ),
    };
