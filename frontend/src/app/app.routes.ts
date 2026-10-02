import { Routes } from '@angular/router';
import { guardAutenticado, guardInvitado, guardRol } from './core/auth/guards';
import { ROLES_GESTION, ROLES_INFORMES } from './core/auth/modelos';
import { ShellGestion, ShellInformes, ShellPublico } from './layout/shells';

/**
 * Estructura (igual que la web original):
 *  - /login, /descargar: públicas, sin navegación.
 *  - /usuarios: área SEPARADA de gestión (solo super_admin), con encabezado propio.
 *  - resto: web de informes (admin y super_admin).
 * Los guards solo mejoran la experiencia: la autorización real la impone la API.
 */
export const routes: Routes = [
  // La ruta protegida de '/' va primero: la pública no tiene hijo para '/', y si fuera antes la raíz quedaría en blanco
  // en vez de redirigir a /login.
  {
    path: '',
    component: ShellInformes,
    canActivate: [guardAutenticado, guardRol(ROLES_INFORMES)],
    children: [
      { path: '', pathMatch: 'full', title: 'Tablero', loadComponent: () => import('./features/dashboard/tablero').then((m) => m.Tablero) },
      {
        path: 'movilizados',
        title: 'Unidades movilizadas',
        data: { tipo: 'movilizados', titulo: 'Unidades movilizadas', descripcion: 'Unidades con el informe de sus 3 etapas cerrado.', vacio: 'Aún no hay unidades con las 3 etapas cerradas.' },
        loadComponent: () => import('./features/vehiculos/listado-vehiculos').then((m) => m.ListadoVehiculos),
      },
      {
        path: 'pendientes',
        title: 'Pendientes por movilizar',
        data: { tipo: 'pendientes', titulo: 'Pendientes por movilizar', descripcion: 'Unidades cargadas que aún no completan sus 3 etapas.', vacio: 'No hay unidades pendientes.' },
        loadComponent: () => import('./features/vehiculos/listado-vehiculos').then((m) => m.ListadoVehiculos),
      },
      {
        path: 'novedades',
        title: 'Unidades con novedad',
        data: { tipo: 'novedad', titulo: 'Unidades con novedad', descripcion: 'Unidades con al menos un hallazgo de daño registrado.', vacio: 'No hay unidades con novedad.' },
        loadComponent: () => import('./features/vehiculos/listado-vehiculos').then((m) => m.ListadoVehiculos),
      },
      { path: 'inventario', title: 'Inventario', loadComponent: () => import('./features/vehiculos/inventario').then((m) => m.Inventario) },
      { path: 'inspecciones', title: 'Informes', loadComponent: () => import('./features/inspecciones/lista-inspecciones').then((m) => m.ListaInspecciones) },
      { path: 'inspecciones/:id', title: 'Informe', loadComponent: () => import('./features/inspecciones/informe-detalle').then((m) => m.InformeDetalle) },
      { path: 'cargar-vins', title: 'Cargar VINs', loadComponent: () => import('./features/cargar-vins/cargar-vins').then((m) => m.CargarVins) },
      { path: 'sofasa', title: 'Órdenes Sofasa', loadComponent: () => import('./features/sofasa/ordenes-sofasa').then((m) => m.OrdenesSofasa) },
    ],
  },
  {
    path: '',
    component: ShellPublico,
    children: [
      { path: 'descargar', title: 'Descargar app', loadComponent: () => import('./features/descargar/descargar').then((m) => m.Descargar) },
      { path: 'login', canActivate: [guardInvitado], title: 'Iniciar sesión', loadComponent: () => import('./features/auth/login').then((m) => m.Login) },
      { path: 'acceso-restringido', canActivate: [guardAutenticado], title: 'Acceso restringido', loadComponent: () => import('./features/auth/paginas-estado').then((m) => m.AccesoRestringido) },
    ],
  },
  {
    path: 'usuarios',
    component: ShellGestion,
    canActivate: [guardAutenticado, guardRol(ROLES_GESTION)],
    children: [{ path: '', title: 'Usuarios', loadComponent: () => import('./features/usuarios/usuarios').then((m) => m.Usuarios) }],
  },
  {
    path: '**',
    component: ShellPublico,
    children: [{ path: '**', title: 'Página no encontrada', loadComponent: () => import('./features/auth/paginas-estado').then((m) => m.NoEncontrado) }],
  },
];
