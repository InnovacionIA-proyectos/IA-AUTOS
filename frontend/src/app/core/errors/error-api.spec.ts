import { HttpErrorResponse } from '@angular/common/http';
import { ErrorApi, mensajeDeError } from './error-api';

describe('ErrorApi', () => {
  it('toma mensaje, código y detalles del cuerpo uniforme de la API', () => {
    const http = new HttpErrorResponse({
      status: 400,
      error: { error: 'Datos inválidos', code: 'VALIDATION_ERROR', message: 'Datos inválidos', details: { fieldErrors: { email: ['Email inválido'] } } },
    });

    const error = ErrorApi.desde(http);

    expect(error.message).toBe('Datos inválidos');
    expect(error.codigo).toBe('VALIDATION_ERROR');
    expect(error.estado).toBe(400);
    expect(error.detalles).toEqual({ fieldErrors: { email: ['Email inválido'] } });
  });

  it('un error de red (estado 0) da un mensaje comprensible', () => {
    const error = ErrorApi.desde(new HttpErrorResponse({ status: 0 }));

    expect(error.codigo).toBe('RED');
    expect(error.message).toContain('No se pudo conectar');
  });

  it('sin cuerpo usa el texto del estado', () => {
    const error = ErrorApi.desde(new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }));

    expect(error.message).toBe('Service Unavailable');
    expect(error.codigo).toBe('ERROR');
  });

  it('mensajeDeError acepta ErrorApi, Error comunes y valores desconocidos', () => {
    expect(mensajeDeError(new ErrorApi('x', 'C', 400))).toBe('x');
    expect(mensajeDeError(new Error('y'))).toBe('y');
    expect(mensajeDeError(42)).toBe('Error de conexión');
    expect(mensajeDeError(null, 'fallo')).toBe('fallo');
  });
});
