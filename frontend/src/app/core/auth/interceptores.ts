import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ServicioConfiguracion } from '../config/servicio-configuracion';
import { ErrorApi } from '../errors/error-api';
import { SesionService } from './sesion.service';

const RUTA_LOGIN = '/api/auth/login';

function esLlamadaApi(url: string, apiUrl: string): boolean {
  return apiUrl ? url.startsWith(apiUrl) : url.startsWith('/api/');
}

/** Adjunta el JWT como Bearer solo en las llamadas a la API (nunca a terceros). */
export const interceptorAutenticacion: HttpInterceptorFn = (peticion, siguiente) => {
  const token = inject(SesionService).token();
  const apiUrl = inject(ServicioConfiguracion).apiUrl;
  if (token && esLlamadaApi(peticion.url, apiUrl)) {
    return siguiente(peticion.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
  }
  return siguiente(peticion);
};

/**
 * Convierte los errores HTTP en ErrorApi y centraliza el 401: una sesión vencida o un usuario retirado limpian la sesión
 * y llevan al login (salvo en el propio login, donde un 401 significa credenciales incorrectas).
 */
export const interceptorErrores: HttpInterceptorFn = (peticion, siguiente) => {
  const sesion = inject(SesionService);
  const router = inject(Router);
  return siguiente(peticion).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        const esLogin = peticion.url.endsWith(RUTA_LOGIN);
        if (error.status === 401 && !esLogin && sesion.autenticado()) {
          sesion.limpiar();
          void router.navigate(['/login'], { queryParams: { motivo: 'expirada' } });
        }
        return throwError(() => ErrorApi.desde(error));
      }
      return throwError(() => error);
    }),
  );
};
