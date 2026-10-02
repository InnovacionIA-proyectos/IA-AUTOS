import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ServicioConfiguracion } from '../config/servicio-configuracion';
import { ErrorApi } from '../errors/error-api';
import { interceptorAutenticacion, interceptorErrores } from './interceptores';
import { SesionService } from './sesion.service';

const API = 'https://api.ejemplo.com';

describe('interceptores HTTP', () => {
  let http: HttpClient;
  let controlador: HttpTestingController;
  let sesion: SesionService;
  const navegar = vi.fn().mockResolvedValue(true);

  beforeEach(() => {
    sessionStorage.clear();
    navegar.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([interceptorAutenticacion, interceptorErrores])),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigate: navegar } },
      ],
    });
    TestBed.inject(ServicioConfiguracion).establecer({ apiUrl: API });
    http = TestBed.inject(HttpClient);
    controlador = TestBed.inject(HttpTestingController);
    sesion = TestBed.inject(SesionService);
  });

  afterEach(() => controlador.verify());

  it('adjunta el Bearer en las llamadas a la API cuando hay sesión', () => {
    sesion.establecer({ token: 'jwt-1', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' } });

    http.get(`${API}/api/usuarios`).subscribe();

    const peticion = controlador.expectOne(`${API}/api/usuarios`);
    expect(peticion.request.headers.get('Authorization')).toBe('Bearer jwt-1');
    peticion.flush([]);
  });

  it('no adjunta el token a servidores de terceros', () => {
    sesion.establecer({ token: 'jwt-1', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' } });

    http.get('https://otro-sitio.com/datos').subscribe();

    const peticion = controlador.expectOne('https://otro-sitio.com/datos');
    expect(peticion.request.headers.has('Authorization')).toBe(false);
    peticion.flush({});
  });

  it('sin sesión no envía Authorization', () => {
    http.get(`${API}/api/usuarios`).subscribe();

    const peticion = controlador.expectOne(`${API}/api/usuarios`);
    expect(peticion.request.headers.has('Authorization')).toBe(false);
    peticion.flush([]);
  });

  it('convierte los errores HTTP en ErrorApi con el mensaje de la API', () => {
    let capturado: unknown;
    http.get(`${API}/api/x`).subscribe({ error: (e) => (capturado = e) });

    controlador.expectOne(`${API}/api/x`).flush({ code: 'NOT_FOUND', message: 'No existe' }, { status: 404, statusText: 'Not Found' });

    expect(capturado).toBeInstanceOf(ErrorApi);
    expect((capturado as ErrorApi).message).toBe('No existe');
    expect((capturado as ErrorApi).estado).toBe(404);
  });

  it('un 401 con sesión la limpia y lleva al login indicando que expiró', () => {
    sesion.establecer({ token: 'vencido', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' } });
    http.get(`${API}/api/usuarios`).subscribe({ error: () => undefined });

    controlador.expectOne(`${API}/api/usuarios`).flush({ message: 'Token inválido' }, { status: 401, statusText: 'Unauthorized' });

    expect(sesion.autenticado()).toBe(false);
    expect(navegar).toHaveBeenCalledWith(['/login'], { queryParams: { motivo: 'expirada' } });
  });

  it('un 401 en el propio login significa credenciales incorrectas: no redirige', () => {
    http.post(`${API}/api/auth/login`, {}).subscribe({ error: () => undefined });

    controlador.expectOne(`${API}/api/auth/login`).flush({ message: 'Correo o contraseña incorrectos' }, { status: 401, statusText: 'Unauthorized' });

    expect(navegar).not.toHaveBeenCalled();
  });

  it('un 403 no cierra la sesión', () => {
    sesion.establecer({ token: 'ok', usuario: { id: '1', nombre: 'A', email: 'a@e.co', rol: 'admin' } });
    http.get(`${API}/api/usuarios`).subscribe({ error: () => undefined });

    controlador.expectOne(`${API}/api/usuarios`).flush({ message: 'Sin permisos' }, { status: 403, statusText: 'Forbidden' });

    expect(sesion.autenticado()).toBe(true);
    expect(navegar).not.toHaveBeenCalled();
  });
});
