import type { Rol } from '../api/api-tipos';

export interface UsuarioSesion {
  id: string;
  nombre: string;
  email: string;
  rol: Rol;
}

export interface Sesion {
  token: string;
  usuario: UsuarioSesion;
}

export interface Credenciales {
  email: string;
  password: string;
}

/** Roles con acceso a cada área de la web. */
export const ROLES_INFORMES: readonly Rol[] = ['admin', 'super_admin'];
export const ROLES_GESTION: readonly Rol[] = ['super_admin'];
