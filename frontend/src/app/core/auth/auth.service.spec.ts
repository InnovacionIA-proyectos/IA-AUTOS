import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ServicioConfiguracion } from '../config/servicio-configuracion';
import { AuthService } from './auth.service';
import { PROVEEDOR_AUTENTICACION, ProveedorLoginPropio, ProveedorSsoNoConfigurado } from './proveedor-autenticacion';
import { SesionService } from './sesion.service';

describe('AuthService con login propio', () => {
  let auth: AuthService;
  let sesion: SesionService;
  let controlador: HttpTestingController;
  const navegar = vi.fn().mockResolvedValue(true);

  beforeEach(() => {
    sessionStorage.clear();
    navegar.mockClear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: Router, useValue: { navigate: navegar } }],
    });
    TestBed.inject(ServicioConfiguracion).establecer({ apiUrl: 'https://api.ejemplo.com' });
    auth = TestBed.inject(AuthService);
    sesion = TestBed.inject(SesionService);
    controlador = TestBed.inject(HttpTestingController);
  });

  it('inicia sesión contra POST /api/auth/login y guarda token y usuario', async () => {
    const promesa = auth.iniciarSesion({ email: '  Ana@Ejemplo.com ', password: 'Clave-Segura-1' });

    const peticion = controlador.expectOne('https://api.ejemplo.com/api/auth/login');
    expect(peticion.request.method).toBe('POST');
    expect(peticion.request.body).toEqual({ email: 'Ana@Ejemplo.com', password: 'Clave-Segura-1' });
    peticion.flush({ token: 'jwt', usuario: { id: 'u1', nombre: 'Ana', email: 'ana@ejemplo.com', rol: 'super_admin' } });
    await promesa;

    expect(sesion.autenticado()).toBe(true);
    expect(sesion.token()).toBe('jwt');
    expect(sesion.rol()).toBe('super_admin');
  });

  it('si el login falla no deja sesión y propaga el error', async () => {
    const promesa = auth.iniciarSesion({ email: 'a@e.co', password: 'mala-clave' });
    controlador.expectOne('https://api.ejemplo.com/api/auth/login').flush({ message: 'x' }, { status: 401, statusText: 'Unauthorized' });

    await expect(promesa).rejects.toBeDefined();
    expect(sesion.autenticado()).toBe(false);
  });

  it('cerrarSesion limpia la sesión y vuelve al login con el motivo', async () => {
    sesion.establecer({ token: 't', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' } });

    await auth.cerrarSesion('inactividad');

    expect(sesion.autenticado()).toBe(false);
    expect(navegar).toHaveBeenCalledWith(['/login'], { queryParams: { motivo: 'inactividad' } });
  });

  it('cerrarSesion sin motivo no agrega parámetros', async () => {
    await auth.cerrarSesion();

    expect(navegar).toHaveBeenCalledWith(['/login'], { queryParams: {} });
  });

  it('destinoSeguro solo admite rutas internas (sin redirecciones abiertas)', () => {
    expect(AuthService.destinoSeguro('/inventario?vin=1')).toBe('/inventario?vin=1');
    expect(AuthService.destinoSeguro(null)).toBe('/');
    expect(AuthService.destinoSeguro('')).toBe('/');
    expect(AuthService.destinoSeguro('https://malo.com')).toBe('/');
    expect(AuthService.destinoSeguro('//malo.com')).toBe('/');
    expect(AuthService.destinoSeguro('/ok?next=https://malo.com')).toBe('/');
    expect(AuthService.destinoSeguro('inventario')).toBe('/');
  });
});

describe('selección del proveedor de autenticación', () => {
  it('usa el login propio por defecto', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });

    expect(TestBed.inject(PROVEEDOR_AUTENTICACION)).toBeInstanceOf(ProveedorLoginPropio);
  });

  it('con proveedorLogin = sso usa el proveedor SSO, que avisa claramente que no está configurado', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(ServicioConfiguracion).establecer({ proveedorLogin: 'sso' });

    const proveedor = TestBed.inject(PROVEEDOR_AUTENTICACION);

    expect(proveedor).toBeInstanceOf(ProveedorSsoNoConfigurado);
    await expect(proveedor.iniciarSesion({ email: 'a', password: 'b' })).rejects.toThrow(/SSO/);
  });
});
