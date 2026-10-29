import 'package:flutter/material.dart';

import '../features/auth/domain/sesion.dart';

/// Las pestañas de la app y la capacidad que exige cada una.
///
/// Una sola lista para las tres cosas que tienen que concordar: lo que muestra la barra de abajo, a dónde se llega
/// al entrar y qué rutas se dejan abrir. Separarlas fue siempre el origen de que el menú ofrezca lo que el
/// servidor luego rechaza.
///
/// Sofasa pide `gestionar_viajes` —admin, super administrador y coordinador de calidad—. Es una decisión de la
/// app, no del servidor: `GET /api/sofasa/ordenes` no restringe por rol y ninguna capacidad habla de esa pantalla.
/// Son las órdenes de carga de la planta, insumo de quien arma viajes; al conductor le toca su viaje y sus
/// unidades. Cuando se defina de quién es esa pantalla, se cambia aquí.
enum Pestana {
  viajes(
    ruta: '/viajes',
    etiqueta: 'Mis viajes',
    capacidad: Capacidad.verViajes,
    icono: Icons.local_shipping_outlined,
    iconoActivo: Icons.local_shipping,
  ),
  inspecciones(
    ruta: '/inspecciones',
    etiqueta: 'Inspecciones',
    capacidad: Capacidad.verInspecciones,
    icono: Icons.assignment_outlined,
    iconoActivo: Icons.assignment,
  ),
  sofasa(
    ruta: '/sofasa',
    etiqueta: 'Sofasa',
    capacidad: Capacidad.gestionarViajes,
    icono: Icons.inventory_2_outlined,
    iconoActivo: Icons.inventory_2,
  ),
  avisos(
    ruta: '/avisos',
    etiqueta: 'Avisos',
    capacidad: Capacidad.verAlertas,
    icono: Icons.notifications_outlined,
    iconoActivo: Icons.notifications,
  );

  const Pestana({
    required this.ruta,
    required this.etiqueta,
    required this.capacidad,
    required this.icono,
    required this.iconoActivo,
  });

  final String ruta;
  final String etiqueta;
  final String capacidad;
  final IconData icono;
  final IconData iconoActivo;
}

/// Las pestañas que esta persona puede ver, en el orden de la barra.
///
/// El conductor no ve «Inspecciones»: lo suyo es su viaje —reportar novedades y confirmar la llegada de cada
/// unidad— y la lista de informes no le sirve de nada. Sus informes los abre desde la unidad, en «Mis viajes».
List<Pestana> pestanasDe(Usuario? usuario) => usuario == null
    ? const []
    : [
        for (final p in Pestana.values)
          if (usuario.puede(p.capacidad) && !(usuario.rol == 'conductor' && p == Pestana.inspecciones)) p,
      ];

/// A dónde llega al entrar: su primera pestaña. Si no tuviera ninguna va igual a la primera, que le mostrará
/// «sin permisos» en vez de dejarlo en una pantalla en blanco.
String destinoInicial(Usuario? usuario) {
  final visibles = pestanasDe(usuario);
  return visibles.isEmpty ? Pestana.values.first.ruta : visibles.first.ruta;
}
