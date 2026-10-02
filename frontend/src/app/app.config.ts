import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { interceptorAutenticacion, interceptorErrores } from './core/auth/interceptores';
import { ServicioConfiguracion } from './core/config/servicio-configuracion';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([interceptorAutenticacion, interceptorErrores])),
    // Carga config.json antes de arrancar: un mismo build sirve para todos los entornos.
    provideAppInitializer(() => inject(ServicioConfiguracion).cargar()),
  ],
};
