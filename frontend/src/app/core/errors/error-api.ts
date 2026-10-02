import { HttpErrorResponse } from '@angular/common/http';

/** Error de la API con el formato uniforme { error, code, message, timestamp, details }. */
export class ErrorApi extends Error {
  constructor(
    mensaje: string,
    readonly codigo: string,
    readonly estado: number,
    readonly detalles?: unknown,
  ) {
    super(mensaje);
    this.name = 'ErrorApi';
  }

  /** Convierte la respuesta HTTP de error en un ErrorApi legible. */
  static desde(error: HttpErrorResponse): ErrorApi {
    const cuerpo = (error.error ?? {}) as { message?: string; code?: string; details?: unknown };
    if (error.status === 0) {
      return new ErrorApi('No se pudo conectar con el servidor. Revisa tu conexión.', 'RED', 0);
    }
    return new ErrorApi(cuerpo.message ?? error.statusText ?? 'Error', cuerpo.code ?? 'ERROR', error.status, cuerpo.details);
  }
}

/** Mensaje presentable de cualquier error. */
export function mensajeDeError(error: unknown, porDefecto = 'Error de conexión'): string {
  if (error instanceof ErrorApi) return error.message;
  if (error instanceof Error && error.message) return error.message;
  return porDefecto;
}
