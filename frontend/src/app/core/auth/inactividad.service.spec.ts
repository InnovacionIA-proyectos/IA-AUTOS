import { TestBed } from '@angular/core/testing';
import { ServicioConfiguracion } from '../config/servicio-configuracion';
import { AuthService } from './auth.service';
import { InactividadService } from './inactividad.service';
import { SesionService } from './sesion.service';

const SESION = { token: 't', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' as const } };

describe('InactividadService', () => {
  const cerrarSesion = vi.fn().mockResolvedValue(undefined);

  function preparar(minutos: number) {
    sessionStorage.clear();
    cerrarSesion.mockClear();
    TestBed.configureTestingModule({ providers: [{ provide: AuthService, useValue: { cerrarSesion } }] });
    TestBed.inject(ServicioConfiguracion).establecer({ inactividadMinutos: minutos });
    const sesion = TestBed.inject(SesionService);
    TestBed.inject(InactividadService);
    return sesion;
  }

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('cierra la sesión por inactividad tras el tiempo configurado', () => {
    const sesion = preparar(30);
    sesion.establecer(SESION);
    TestBed.tick();

    vi.advanceTimersByTime(29 * 60_000);
    expect(cerrarSesion).not.toHaveBeenCalled();
    vi.advanceTimersByTime(60_000 + 1);

    expect(cerrarSesion).toHaveBeenCalledWith('inactividad');
  });

  it('la actividad del usuario reinicia el temporizador', () => {
    const sesion = preparar(10);
    sesion.establecer(SESION);
    TestBed.tick();

    vi.advanceTimersByTime(9 * 60_000);
    document.dispatchEvent(new Event('keydown'));
    vi.advanceTimersByTime(9 * 60_000);
    expect(cerrarSesion).not.toHaveBeenCalled();

    vi.advanceTimersByTime(2 * 60_000);
    expect(cerrarSesion).toHaveBeenCalledTimes(1);
  });

  it('sin sesión no hay temporizador', () => {
    preparar(1);
    TestBed.tick();

    vi.advanceTimersByTime(10 * 60_000);

    expect(cerrarSesion).not.toHaveBeenCalled();
  });

  it('al cerrar la sesión se detiene el temporizador', () => {
    const sesion = preparar(1);
    sesion.establecer(SESION);
    TestBed.tick();
    sesion.limpiar();
    TestBed.tick();

    vi.advanceTimersByTime(5 * 60_000);

    expect(cerrarSesion).not.toHaveBeenCalled();
  });

  it('con 0 minutos el cierre por inactividad está desactivado', () => {
    const sesion = preparar(0);
    sesion.establecer(SESION);
    TestBed.tick();

    vi.advanceTimersByTime(24 * 60 * 60_000);

    expect(cerrarSesion).not.toHaveBeenCalled();
  });
});
