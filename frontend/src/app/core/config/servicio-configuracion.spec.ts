import { TestBed } from '@angular/core/testing';
import { ServicioConfiguracion } from './servicio-configuracion';

describe('ServicioConfiguracion', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('usa valores por defecto seguros', () => {
    const c = TestBed.inject(ServicioConfiguracion).configuracion;
    expect(c).toEqual({ apiUrl: '', apkUrl: '', inactividadMinutos: 30, proveedorLogin: 'propio' });
  });

  it('quita la barra final de la URL de la API', () => {
    const servicio = TestBed.inject(ServicioConfiguracion);
    servicio.establecer({ apiUrl: 'https://api.ejemplo.com///' });
    expect(servicio.apiUrl).toBe('https://api.ejemplo.com');
  });

  it('solo acepta los proveedores "propio" y "sso"', () => {
    const servicio = TestBed.inject(ServicioConfiguracion);
    servicio.establecer({ proveedorLogin: 'sso' });
    expect(servicio.configuracion.proveedorLogin).toBe('sso');
    servicio.establecer({ proveedorLogin: 'otro' as never });
    expect(servicio.configuracion.proveedorLogin).toBe('propio');
  });

  it('carga config.json y conserva los valores no enviados', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ apiUrl: 'https://api.ejemplo.com', inactividadMinutos: 5 }) }));
    const servicio = TestBed.inject(ServicioConfiguracion);

    await servicio.cargar();

    expect(servicio.configuracion).toEqual({ apiUrl: 'https://api.ejemplo.com', apkUrl: '', inactividadMinutos: 5, proveedorLogin: 'propio' });
    expect(fetch).toHaveBeenCalledWith('config.json', { cache: 'no-store' });
  });

  it('si config.json falta o falla, no rompe el arranque', async () => {
    const servicio = TestBed.inject(ServicioConfiguracion);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false }));
    await expect(servicio.cargar()).resolves.toBeUndefined();
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('sin red')));
    await expect(servicio.cargar()).resolves.toBeUndefined();
    expect(servicio.configuracion.inactividadMinutos).toBe(30);
  });
});
