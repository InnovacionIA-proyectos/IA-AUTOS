import type { ResourceRef } from '@angular/core';
import { mensajeDeError } from '../../core/errors/error-api';

/** Mensaje de error de un recurso de carga (o cadena vacía si no hay error). */
export function errorDe(recurso: ResourceRef<unknown>): string {
  const error = recurso.error();
  return error ? mensajeDeError(error) : '';
}
