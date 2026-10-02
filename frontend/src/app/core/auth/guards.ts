import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import type { Rol } from '../api/api-tipos';
import { SesionService } from './sesion.service';

/** Exige sesión; si no hay, lleva al login recordando a dónde quería ir. */
export const guardAutenticado: CanActivateFn = (_ruta, estado) => {
  const sesion = inject(SesionService);
  const router = inject(Router);
  return sesion.autenticado() ? true : router.createUrlTree(['/login'], { queryParams: { redirigir: estado.url } });
};

/**
 * Exige que el rol esté permitido. Es solo experiencia de usuario: la autorización real la impone la API
 * (el rol efectivo sale de la base de datos en cada solicitud).
 */
export const guardRol = (roles: readonly Rol[]): CanActivateFn => () => {
  const sesion = inject(SesionService);
  const router = inject(Router);
  return sesion.tieneRol(roles) ? true : router.createUrlTree(['/acceso-restringido']);
};

/** Para el login: si ya hay sesión, no lo muestra. */
export const guardInvitado: CanActivateFn = () => {
  const sesion = inject(SesionService);
  const router = inject(Router);
  return sesion.autenticado() ? router.createUrlTree(['/']) : true;
};
