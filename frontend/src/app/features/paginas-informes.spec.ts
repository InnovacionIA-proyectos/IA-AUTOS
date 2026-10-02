import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { render, screen, waitFor } from '@testing-library/angular';
import { userEvent } from '@testing-library/user-event';
import { of, throwError } from 'rxjs';
import { ApiService } from '../core/api/api.service';
import { ErrorApi } from '../core/errors/error-api';
import { esTrazoSvg, viewBoxDeFirma } from '../shared/util/firma';
import { Tablero } from './dashboard/tablero';
import { InformeDetalle } from './inspecciones/informe-detalle';
import { ListaInspecciones } from './inspecciones/lista-inspecciones';
import { OrdenesSofasa } from './sofasa/ordenes-sofasa';
import { Inventario } from './vehiculos/inventario';
import { ListadoVehiculos } from './vehiculos/listado-vehiculos';

const rutaCon = (datos: object = {}, parametros: Record<string, string> = {}, consulta: Record<string, string> = {}) => ({
  provide: ActivatedRoute,
  useValue: { snapshot: { data: datos, paramMap: convertToParamMap(parametros), queryParamMap: convertToParamMap(consulta) } },
});

const tasa = {
  tasaGlobal: 12.5,
  totalMovilizadas: 8,
  totalPendientes: 3,
  totalConNovedad: 1,
  totalNovedad: 2,
  porOperacion: [{ operacion: 'nuevos', total: 8, conNovedad: 1, tasaActual: 12.5, lineaBase: 10, variacion: 2.5 }],
};

describe('Tablero', () => {
  it('muestra métricas, barras por proceso y la tabla de tasa de rechazo', async () => {
    const api = {
      tasaRechazo: () => of(tasa),
      patrones: () =>
        of({ total: 2, porProceso: [{ proceso: 'cargue', total: 2 }], porZona: [{ parte_vehiculo: 'puerta_izq', total: 2 }], porZonaProceso: [] }),
    };
    await render(Tablero, { providers: [provideRouter([]), { provide: ApiService, useValue: api }] });

    expect(await screen.findByText('Unidades movilizadas')).toBeTruthy();
    expect(screen.getByText('12.5%', { selector: 'p' })).toBeTruthy();
    expect(screen.getByText('Cargue')).toBeTruthy();
    expect(screen.getByText('Puerta izq')).toBeTruthy();
    expect(screen.getByText('+2.5%')).toBeTruthy();
  });

  it('sin VINs cargados explica qué hacer', async () => {
    const api = { tasaRechazo: () => of({ ...tasa, totalMovilizadas: 0, totalPendientes: 0 }), patrones: () => of({ total: 0, porProceso: [], porZona: [], porZonaProceso: [] }) };
    await render(Tablero, { providers: [provideRouter([]), { provide: ApiService, useValue: api }] });

    expect(await screen.findByText('Aún no hay VINs cargados')).toBeTruthy();
  });

  it('muestra el error de la API y permite reintentar', async () => {
    const api = { tasaRechazo: () => throwError(() => new ErrorApi('Falla del servidor', 'INTERNAL_ERROR', 500)), patrones: () => of({ total: 0, porProceso: [], porZona: [], porZonaProceso: [] }) };
    await render(Tablero, { providers: [provideRouter([]), { provide: ApiService, useValue: api }] });

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeTruthy();
  });
});

describe('ListadoVehiculos', () => {
  const fila = { id: '1', vin: 'VIN00000000000001', marca: 'Renault', modelo: 'Duster', color: null, operacion: 'nuevos', etapas: 3, completas: 2, novedades: 0 };
  const datos = { tipo: 'pendientes', titulo: 'Pendientes por movilizar', descripcion: 'desc', vacio: 'No hay unidades pendientes.' };

  it('lista las unidades del tipo de la ruta y abre el inventario al hacer clic', async () => {
    const listarVehiculos = vi.fn().mockReturnValue(of({ rows: [fila], total: 1, page: 1, pageSize: 20 }));
    const { fixture } = await render(ListadoVehiculos, {
      providers: [provideRouter([]), { provide: ApiService, useValue: { listarVehiculos } }, rutaCon(datos)],
    });
    const navegar = vi.spyOn(fixture.debugElement.injector.get(Router), 'navigate').mockResolvedValue(true);

    expect(await screen.findByText('VIN00000000000001')).toBeTruthy();
    expect(screen.getByText('2 / 3')).toBeTruthy();
    expect(screen.getByText('Sin novedad')).toBeTruthy();
    expect(listarVehiculos).toHaveBeenCalledWith('pendientes', '', 1);

    await userEvent.click(screen.getByText('VIN00000000000001'));
    expect(navegar).toHaveBeenCalledWith(['/inventario'], { queryParams: { vin: 'VIN00000000000001' } });
  });

  it('muestra el estado vacío de la ruta cuando no hay unidades', async () => {
    const listarVehiculos = vi.fn().mockReturnValue(of({ rows: [], total: 0, page: 1, pageSize: 20 }));
    await render(ListadoVehiculos, { providers: [provideRouter([]), { provide: ApiService, useValue: { listarVehiculos } }, rutaCon(datos)] });

    expect(await screen.findByText('No hay unidades pendientes.')).toBeTruthy();
  });

  it('pagina cuando hay más de una página', async () => {
    const listarVehiculos = vi.fn().mockReturnValue(of({ rows: [fila], total: 45, page: 1, pageSize: 20 }));
    await render(ListadoVehiculos, { providers: [provideRouter([]), { provide: ApiService, useValue: { listarVehiculos } }, rutaCon(datos)] });

    expect(await screen.findByText(/Página 1 de 3/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
    await waitFor(() => expect(listarVehiculos).toHaveBeenLastCalledWith('pendientes', '', 2));
  });

  it('busca por VIN tras una pausa y vuelve a la primera página', async () => {
    const listarVehiculos = vi.fn().mockReturnValue(of({ rows: [], total: 0, page: 1, pageSize: 20 }));
    await render(ListadoVehiculos, { providers: [provideRouter([]), { provide: ApiService, useValue: { listarVehiculos } }, rutaCon(datos)] });

    await userEvent.type(screen.getByLabelText('Buscar por VIN'), 'ABC');
    await waitFor(() => expect(listarVehiculos).toHaveBeenLastCalledWith('pendientes', 'ABC', 1));
  });
});

describe('Inventario', () => {
  const inventario = {
    id: '1',
    vin: 'VIN00000000000001',
    marca: 'Renault',
    tipo_vehiculo: 'SUV',
    modelo: 'Duster',
    chasis: null,
    color: 'Rojo',
    operacion: 'nuevos',
    etapas: [
      {
        id: 'e1',
        orden: 1,
        proceso: 'cargue',
        requiere_inspeccion: true,
        inspecciones: [{ id: 'i1', momento: 'informe', fecha: '2026-01-02T10:00:00Z', estado: 'cerrada', conductor_nombre: 'Ana', hallazgos: [{ id: 'h' }], evidencias: [] }],
      },
      { id: 'e2', orden: 2, proceso: 'transporte', requiere_inspeccion: true, inspecciones: [] },
    ],
  };

  it('sin VIN invita a buscar', async () => {
    await render(Inventario, { providers: [provideRouter([]), { provide: ApiService, useValue: {} }, rutaCon()] });
    expect(screen.getByText('Busca un VIN')).toBeTruthy();
  });

  it('carga el inventario del VIN de la URL con sus etapas e informes', async () => {
    const api = { inventario: vi.fn().mockReturnValue(of(inventario)) };
    await render(Inventario, { providers: [provideRouter([]), { provide: ApiService, useValue: api }, rutaCon({}, {}, { vin: 'VIN00000000000001' })] });

    expect(await screen.findByText('1. Cargue')).toBeTruthy();
    expect(api.inventario).toHaveBeenCalledWith('VIN00000000000001');
    expect(screen.getByText('1 hallazgo(s)')).toBeTruthy();
    expect(screen.getByText('Sin informe registrado todavía.')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Ver informe' }).getAttribute('href')).toBe('/inspecciones/i1');
  });

  it('descarga el PDF con el cliente autenticado', async () => {
    const inventarioPdf = vi.fn().mockReturnValue(of(new Blob(['%PDF'])));
    const api = { inventario: () => of(inventario), inventarioPdf };
    const crear = vi.fn().mockReturnValue('blob:x');
    const revocar = vi.fn();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: crear, revokeObjectURL: revocar }));
    await render(Inventario, { providers: [provideRouter([]), { provide: ApiService, useValue: api }, rutaCon({}, {}, { vin: 'VIN00000000000001' })] });

    await userEvent.click(await screen.findByRole('button', { name: 'Descargar PDF' }));
    await waitFor(() => expect(revocar).toHaveBeenCalledWith('blob:x'));
    expect(inventarioPdf).toHaveBeenCalledWith('VIN00000000000001');
  });
});

describe('ListaInspecciones', () => {
  const inspeccion = { id: 'i1', vin: 'VIN00000000000001', proceso: 'descargue', momento: 'informe', fecha: '2026-01-02T10:00:00Z', conductor_nombre: 'Ana', estado: 'cerrada' };

  it('lista los informes y abre el detalle', async () => {
    const listarInspecciones = vi.fn().mockReturnValue(of([inspeccion]));
    const { fixture } = await render(ListaInspecciones, { providers: [provideRouter([]), { provide: ApiService, useValue: { listarInspecciones } }] });
    const navegar = vi.spyOn(fixture.debugElement.injector.get(Router), 'navigate').mockResolvedValue(true);

    expect(await screen.findByText('Descargue')).toBeTruthy();
    expect(screen.getByText('Cerrada', { selector: 'app-insignia' })).toBeTruthy();
    await userEvent.click(screen.getByText('VIN00000000000001'));
    expect(navegar).toHaveBeenCalledWith(['/inspecciones', 'i1']);
  });

  it('aplica el filtro de estado', async () => {
    const listarInspecciones = vi.fn().mockReturnValue(of([]));
    await render(ListaInspecciones, { providers: [provideRouter([]), { provide: ApiService, useValue: { listarInspecciones } }] });

    await userEvent.selectOptions(screen.getByLabelText('Estado'), 'borrador');
    await waitFor(() => expect(listarInspecciones).toHaveBeenLastCalledWith({ vin: '', estado: 'borrador', proceso: '' }));
  });
});

describe('InformeDetalle', () => {
  const informe = {
    id: 'i1',
    momento: 'informe',
    fecha: '2026-01-02T10:00:00Z',
    lat: 6.2,
    lng: -75.5,
    estado: 'cerrada',
    conductor_nombre: 'Ana',
    placa: 'ABC123',
    trailer: null,
    notas: 'Sin observaciones',
    vin: 'VIN00000000000001',
    marca: 'Renault',
    modelo: 'Duster',
    proceso: 'cargue',
    inspector_nombre: 'Luis',
    ninera: null,
    hallazgos: [{ id: 'h1', parte_vehiculo: 'puerta_izq', clase_dano: 'rayon', tamano_dano: 'pequeno' }],
    evidencias: [{ id: 'ev1', url: '/files/a.jpg', sello_fecha: '2026-01-02T10:00:00Z' }],
    transferencia: { firmada: true, firmada_at: '2026-01-02T11:00:00Z', entrega_nombre: 'Ana', recibe_nombre: 'Pedro', firma_entrega: 'M 10 10 L 50 40', firma_recibe: 'legado' },
  };
  const api = { informe: () => of(informe), urlArchivo: (r: string) => `https://api.test${r}` };

  it('muestra datos, hallazgos, evidencias con URL absoluta y firmas', async () => {
    await render(InformeDetalle, { providers: [provideRouter([]), { provide: ApiService, useValue: api }, rutaCon({}, { id: 'i1' })] });

    expect(await screen.findByText('Informe de Cargue')).toBeTruthy();
    expect(screen.getByText('Puerta izq')).toBeTruthy();
    expect(screen.getByAltText('Evidencia fotográfica').getAttribute('src')).toBe('https://api.test/files/a.jpg');
    expect(screen.getByRole('img', { name: 'Firma de quien entrega' })).toBeTruthy();
    expect(screen.getByText('Firma registrada (formato anterior).')).toBeTruthy();
  });

  it('un informe inexistente muestra el error de la API', async () => {
    const fallo = { informe: () => throwError(() => new ErrorApi('Informe no encontrado', 'NOT_FOUND', 404)) };
    await render(InformeDetalle, { providers: [provideRouter([]), { provide: ApiService, useValue: fallo }, rutaCon({}, { id: 'x' })] });

    expect(await screen.findByText('Informe no encontrado')).toBeTruthy();
  });
});

describe('OrdenesSofasa', () => {
  it('lista las órdenes y filtra por ciudad', async () => {
    const listarSofasa = vi.fn().mockReturnValue(of([{ id: '1', orden_number: 'OR-1', vin: 'VIN00000000000001', model_description: 'Duster', status_code: 'ABIERTA', origin_cedi: 'Envigado', destination_city: 'Cali', loading_date: null, datetime_event: null }]));
    await render(OrdenesSofasa, { providers: [{ provide: ApiService, useValue: { listarSofasa } }] });

    expect(await screen.findByText('OR-1')).toBeTruthy();
    await userEvent.type(screen.getByLabelText('Ciudad de destino'), 'Cali');
    await waitFor(() => expect(listarSofasa).toHaveBeenLastCalledWith({ vin: '', estado: '', ciudad: 'Cali' }));
  });
});

describe('firma', () => {
  it('reconoce trazos SVG y descarta identificadores heredados', () => {
    expect(esTrazoSvg('M 1 2 L 3 4')).toBe(true);
    expect(esTrazoSvg('firma-123')).toBe(false);
    expect(esTrazoSvg(null)).toBe(false);
  });

  it('ajusta el viewBox al trazo con margen y tiene un valor por defecto', () => {
    expect(viewBoxDeFirma('M 0 0 L 100 50')).toBe('-12 -12 124 74');
    expect(viewBoxDeFirma('')).toBe('0 0 340 160');
  });
});
