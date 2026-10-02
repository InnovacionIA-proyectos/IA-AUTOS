import { TestBed } from '@angular/core/testing';
import type { Sesion } from './modelos';
import { CLAVE_SESION, SesionService } from './sesion.service';

const SESION: Sesion = { token: 'jwt-de-prueba', usuario: { id: '1', nombre: 'Ana', email: 'ana@ejemplo.com', rol: 'admin' } };

function nuevoServicio(): SesionService {
  TestBed.resetTestingModule();
  return TestBed.inject(SesionService);
}

describe('SesionService', () => {
  beforeEach(() => sessionStorage.clear());

  it('comienza sin sesión', () => {
    const s = nuevoServicio();
    expect(s.autenticado()).toBe(false);
    expect(s.token()).toBeNull();
    expect(s.usuario()).toBeNull();
    expect(s.rol()).toBeNull();
  });

  it('establecer expone token, usuario y rol, y los guarda en sessionStorage (no en localStorage)', () => {
    const s = nuevoServicio();

    s.establecer(SESION);

    expect(s.autenticado()).toBe(true);
    expect(s.token()).toBe('jwt-de-prueba');
    expect(s.rol()).toBe('admin');
    expect(JSON.parse(sessionStorage.getItem(CLAVE_SESION)!)).toEqual(SESION);
    expect(localStorage.getItem(CLAVE_SESION)).toBeNull();
  });

  it('recupera la sesión tras recargar la pestaña', () => {
    sessionStorage.setItem(CLAVE_SESION, JSON.stringify(SESION));

    const s = nuevoServicio();

    expect(s.autenticado()).toBe(true);
    expect(s.usuario()?.nombre).toBe('Ana');
  });

  it('limpiar borra la sesión del estado y del almacenamiento', () => {
    const s = nuevoServicio();
    s.establecer(SESION);

    s.limpiar();

    expect(s.autenticado()).toBe(false);
    expect(sessionStorage.getItem(CLAVE_SESION)).toBeNull();
  });

  it('ignora datos corruptos o incompletos en el almacenamiento', () => {
    sessionStorage.setItem(CLAVE_SESION, '{no es json');
    expect(nuevoServicio().autenticado()).toBe(false);

    sessionStorage.setItem(CLAVE_SESION, JSON.stringify({ token: 'x' }));
    expect(nuevoServicio().autenticado()).toBe(false);
  });

  it('tieneRol evalúa el rol actual', () => {
    const s = nuevoServicio();
    expect(s.tieneRol(['admin'])).toBe(false);

    s.establecer(SESION);

    expect(s.tieneRol(['admin', 'super_admin'])).toBe(true);
    expect(s.tieneRol(['super_admin'])).toBe(false);
  });
});
