import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { guardAutenticado, guardInvitado, guardRol } from './guards';
import { SesionService } from './sesion.service';

const RUTA = {} as ActivatedRouteSnapshot;
const ESTADO = (url: string) => ({ url }) as RouterStateSnapshot;

function ejecutar<T>(fn: () => T): T {
  return TestBed.runInInjectionContext(fn);
}

describe('guards de rutas', () => {
  let sesion: SesionService;
  let router: Router;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [{ provide: Router, useValue: { createUrlTree: (c: unknown[], extras?: unknown) => ({ comandos: c, extras }) as unknown as UrlTree } }] });
    sesion = TestBed.inject(SesionService);
    router = TestBed.inject(Router);
  });

  it('guardAutenticado deja pasar con sesión', () => {
    sesion.establecer({ token: 't', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' } });

    expect(ejecutar(() => guardAutenticado(RUTA, ESTADO('/inventario')))).toBe(true);
  });

  it('guardAutenticado sin sesión lleva al login recordando el destino', () => {
    const resultado = ejecutar(() => guardAutenticado(RUTA, ESTADO('/inventario?vin=ABC'))) as unknown as { comandos: unknown[]; extras: unknown };

    expect(resultado.comandos).toEqual(['/login']);
    expect(resultado.extras).toEqual({ queryParams: { redirigir: '/inventario?vin=ABC' } });
    expect(router.createUrlTree).toBeDefined();
  });

  it('guardRol permite solo los roles indicados y si no lleva a acceso restringido', () => {
    sesion.establecer({ token: 't', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' } });

    expect(ejecutar(() => guardRol(['admin', 'super_admin'])(RUTA, ESTADO('/')))).toBe(true);
    const denegado = ejecutar(() => guardRol(['super_admin'])(RUTA, ESTADO('/usuarios'))) as unknown as { comandos: unknown[] };
    expect(denegado.comandos).toEqual(['/acceso-restringido']);
  });

  it('guardRol deniega a quien no tiene sesión', () => {
    const resultado = ejecutar(() => guardRol(['admin'])(RUTA, ESTADO('/'))) as unknown as { comandos: unknown[] };

    expect(resultado.comandos).toEqual(['/acceso-restringido']);
  });

  it('guardInvitado muestra el login solo a quien no tiene sesión', () => {
    expect(ejecutar(() => guardInvitado(RUTA, ESTADO('/login')))).toBe(true);

    sesion.establecer({ token: 't', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' } });
    const redirigido = ejecutar(() => guardInvitado(RUTA, ESTADO('/login'))) as unknown as { comandos: unknown[] };
    expect(redirigido.comandos).toEqual(['/']);
  });
});
