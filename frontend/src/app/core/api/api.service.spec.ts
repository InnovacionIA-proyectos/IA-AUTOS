import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ServicioConfiguracion } from '../config/servicio-configuracion';
import { ApiService } from './api.service';

const BASE = 'https://api.ejemplo.com';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(ServicioConfiguracion).establecer({ apiUrl: BASE });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('listarVehiculos envía tipo, página y VIN solo si se indica', () => {
    api.listarVehiculos('pendientes', '', 2).subscribe();
    const a = http.expectOne((r) => r.url === `${BASE}/api/vehiculos/listado`);
    expect(a.request.params.get('tipo')).toBe('pendientes');
    expect(a.request.params.get('page')).toBe('2');
    expect(a.request.params.has('vin')).toBe(false);
    a.flush({});

    api.listarVehiculos('novedad', 'ABC123', 1).subscribe();
    const b = http.expectOne((r) => r.url === `${BASE}/api/vehiculos/listado`);
    expect(b.request.params.get('vin')).toBe('ABC123');
    b.flush({});
  });

  it('codifica el VIN en la ruta del inventario y del PDF', () => {
    api.inventario('A B/1').subscribe();
    http.expectOne(`${BASE}/api/vehiculos/A%20B%2F1/inventario`).flush({});

    api.inventarioPdf('VIN1').subscribe();
    const pdf = http.expectOne(`${BASE}/api/vehiculos/VIN1/inventario/pdf`);
    expect(pdf.request.responseType).toBe('blob');
    pdf.flush(new Blob(['%PDF']));
  });

  it('usuarios: crear, actualizar y eliminar usan el verbo y la ruta del contrato', () => {
    api.crearUsuario({ nombre: 'A', email: 'a@e.co', rol: 'inspector', subtipo_inspector: null, password: 'Clave-Segura-1' }).subscribe();
    expect(http.expectOne(`${BASE}/api/usuarios`).request.method).toBe('POST');

    api.actualizarUsuario('u1', { nombre: 'B', email: null, rol: null, subtipo_inspector: null, password: null, activo: false }).subscribe();
    const patch = http.expectOne(`${BASE}/api/usuarios/u1`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body.activo).toBe(false);

    api.eliminarUsuario('u1').subscribe();
    expect(http.expectOne(`${BASE}/api/usuarios/u1`).request.method).toBe('DELETE');
    http.match(() => true).forEach((r) => r.flush({}));
  });

  it('listarInspecciones y listarSofasa omiten los filtros vacíos', () => {
    api.listarInspecciones({ vin: '', estado: 'cerrada' }).subscribe();
    const i = http.expectOne((r) => r.url === `${BASE}/api/inspecciones`);
    expect(i.request.params.keys()).toEqual(['estado']);
    i.flush([]);

    api.listarSofasa({ vin: 'X', ciudad: '' }).subscribe();
    const s = http.expectOne((r) => r.url === `${BASE}/api/sofasa/ordenes`);
    expect(s.request.params.keys()).toEqual(['vin']);
    s.flush([]);
  });

  it('reportes y perfil usan sus rutas', () => {
    api.patrones().subscribe();
    api.tasaRechazo().subscribe();
    api.perfil().subscribe();
    api.informe('id-1').subscribe();
    api.buscarVehiculos('abc').subscribe();
    api.cargarVins({ items: [] }).subscribe();

    for (const ruta of ['/api/patrones', '/api/metricas/rechazo', '/api/auth/me', '/api/inspecciones/id-1']) {
      http.expectOne(`${BASE}${ruta}`).flush({});
    }
    http.expectOne((r) => r.url === `${BASE}/api/vehiculos` && r.params.get('vin') === 'abc').flush([]);
    expect(http.expectOne(`${BASE}/api/vehiculos/cargar`).request.method).toBe('POST');
  });

  it('urlArchivo resuelve rutas relativas contra la API y respeta las absolutas', () => {
    expect(api.urlArchivo('/api/uploads/x.jpg')).toBe(`${BASE}/api/uploads/x.jpg`);
    expect(api.urlArchivo('https://cdn.ejemplo.com/x.jpg')).toBe('https://cdn.ejemplo.com/x.jpg');
    expect(api.urlArchivo(null)).toBeNull();
    expect(api.urlArchivo('')).toBeNull();
  });
});
