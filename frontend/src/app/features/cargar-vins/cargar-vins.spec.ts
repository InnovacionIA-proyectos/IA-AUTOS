import { render, screen, waitFor } from '@testing-library/angular';
import { userEvent } from '@testing-library/user-event';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { ErrorApi } from '../../core/errors/error-api';
import { CargarVins } from './cargar-vins';
import { ErrorLectura, Hoja, leerArchivo, leerCsv, normalizarCelda } from './lector-excel';
import { MAX_FILAS_CARGA, autoMapear, encabezadosDe, letraColumna, revisarFilas } from './mapeo-vins';
import { LectorExcel } from './servicio-lector-excel';

const hojaAsignacion: Hoja = {
  nombre: 'Asignación',
  filas: [
    ['CHASIS', 'Marca', 'Descripción', 'Color'],
    ['VIN00000000000001', 'Renault', 'Duster', 'Rojo'],
    ['VIN00000000000002', 'Renault', 'Kwid', null],
    [],
    [null, 'Renault', 'Logan', 'Azul'],
    ['VIN00000000000004', null, 'Sandero', 'Gris'],
  ],
};

describe('mapeo de columnas', () => {
  it('detecta VIN (incluso como CHASIS), marca, modelo y color', () => {
    expect(autoMapear(['CHASIS', 'Marca', 'Descripción', 'Color'])).toEqual({ vin: '0', marca: '1', modelo: '2', color: '3' });
    expect(autoMapear(['x', 'y'])).toEqual({ vin: '', marca: '', modelo: '', color: '' });
  });

  it('numera columnas como Excel', () => {
    expect([0, 25, 26, 27, 701, 702].map(letraColumna)).toEqual(['A', 'Z', 'AA', 'AB', 'ZZ', 'AAA']);
  });

  it('toma el ancho de la fila más larga y el encabezado de la fila indicada', () => {
    expect(encabezadosDe([['a'], ['x', 'y', 'z']], 1)).toEqual(['a', '', '']);
    expect(encabezadosDe([['a'], ['x', 'y', 'z']], 2)).toEqual(['x', 'y', 'z']);
  });

  it('revisa filas: omite vacías, conserva el número real y marca lo que falta', () => {
    const revisadas = revisarFilas(hojaAsignacion.filas, 1, { vin: '0', marca: '1', modelo: '2', color: '3' }, '');
    expect(revisadas.map((r) => [r.fila, r.lista, r.faltaVin, r.faltaMarca])).toEqual([
      [2, true, false, false],
      [3, true, false, false],
      [5, false, true, false],
      [6, false, false, true],
    ]);
  });

  it('usa la marca fija cuando no hay columna de marca', () => {
    const revisadas = revisarFilas(hojaAsignacion.filas, 1, { vin: '0', marca: '', modelo: '', color: '' }, ' Toyota ');
    expect(revisadas.filter((r) => r.lista).every((r) => r.marca === 'Toyota')).toBe(true);
  });

  it('el tope por carga coincide con el del API', () => {
    expect(MAX_FILAS_CARGA).toBe(5000);
  });
});

describe('lector de archivos', () => {
  it('lee CSV con separador punto y coma, comillas y BOM', () => {
    const csv = '﻿VIN;Marca;Nota\r\nABC;"Ren;ault";"dice ""hola"""\r\n\r\nDEF;Kia;\r\n';
    expect(leerCsv(csv)).toEqual([
      ['VIN', 'Marca', 'Nota'],
      ['ABC', 'Ren;ault', 'dice "hola"'],
      [],
      ['DEF', 'Kia', null],
    ]);
  });

  it('normaliza celdas de ExcelJS: fórmulas, texto enriquecido, enlaces, fechas y errores', () => {
    expect(normalizarCelda({ formula: 'A1', result: 5 })).toBe(5);
    expect(normalizarCelda({ richText: [{ text: 'Re' }, { text: 'nault' }] })).toBe('Renault');
    expect(normalizarCelda({ text: 'sitio', hyperlink: 'http://x' })).toBe('sitio');
    expect(normalizarCelda(new Date('2026-01-02T03:04:05Z'))).toBe('2026-01-02T03:04:05.000Z');
    expect(normalizarCelda({ error: '#N/A' })).toBeNull();
    expect(normalizarCelda(undefined)).toBeNull();
  });

  it('lee un .xlsx con varias hojas conservando los números de fila', async () => {
    const { Workbook } = await import('exceljs');
    const libro = new Workbook();
    const a = libro.addWorksheet('Datos');
    a.addRow(['VIN', 'Marca']);
    a.getRow(3).values = ['VIN00000000000001', 'Kia'];
    libro.addWorksheet('Otra').addRow(['x']);
    const datos = (await libro.xlsx.writeBuffer()) as ArrayBuffer;

    const hojas = await leerArchivo(datos, 'asignacion.xlsx');

    expect(hojas.map((h) => h.nombre)).toEqual(['Datos', 'Otra']);
    expect(hojas[0].filas).toEqual([['VIN', 'Marca'], [], ['VIN00000000000001', 'Kia']]);
  });

  it('rechaza .xls, formatos desconocidos y archivos corruptos con un mensaje claro', async () => {
    const vacio = new ArrayBuffer(4);
    await expect(leerArchivo(vacio, 'a.xls')).rejects.toThrow(/\.xlsx o \.csv/);
    await expect(leerArchivo(vacio, 'a.pdf')).rejects.toThrow('Formato no compatible');
    await expect(leerArchivo(vacio, 'a.xlsx')).rejects.toBeInstanceOf(ErrorLectura);
  });

  it('el servicio rechaza archivos de más de 10 MB sin leerlos', async () => {
    const grande = { size: 11 * 1024 * 1024, name: 'a.xlsx' } as File;
    await expect(new LectorExcel().leer(grande)).rejects.toThrow('supera el máximo de 10 MB');
  });
});

describe('CargarVins', () => {
  async function subir(leer: ReturnType<typeof vi.fn>, api: object = {}) {
    const vista = await render(CargarVins, {
      providers: [
        { provide: LectorExcel, useValue: { leer } },
        { provide: ApiService, useValue: api },
      ],
    });
    await userEvent.upload(screen.getByLabelText('Archivo de VINs'), new File(['x'], 'asignacion.xlsx'));
    return vista;
  }

  it('al subir un archivo detecta columnas y resume qué filas están listas', async () => {
    await subir(vi.fn().mockResolvedValue([hojaAsignacion]));

    expect(await screen.findByText('Asignación (5 filas)')).toBeTruthy();
    expect(screen.getByText('2 listas')).toBeTruthy();
    expect(screen.getByText('3 filas con VIN')).toBeTruthy();
    expect(screen.getByText('2 se omiten')).toBeTruthy();
    expect(screen.getByRole('button', { name: /Cargar 2 VINs/ })).toBeTruthy();
    expect(screen.getByText('Falta marca')).toBeTruthy();
  });

  it('envía solo las filas listas con la operación elegida y muestra el reporte por VIN', async () => {
    const cargarVins = vi.fn().mockReturnValue(
      of({ recibidos: 2, creados: 1, actualizados: 0, fallidos: 1, errores: [{ vin: 'VIN00000000000002', error: 'VIN inválido' }] }),
    );
    await subir(vi.fn().mockResolvedValue([hojaAsignacion]), { cargarVins });

    await userEvent.selectOptions(await screen.findByLabelText('Operación (para todo el archivo)'), 'exportacion');
    await userEvent.click(screen.getByRole('button', { name: /Cargar 2 VINs/ }));

    await waitFor(() =>
      expect(cargarVins).toHaveBeenCalledWith({
        items: [
          { vin: 'VIN00000000000001', marca: 'Renault', modelo: 'Duster', color: 'Rojo', operacion: 'exportacion' },
          { vin: 'VIN00000000000002', marca: 'Renault', modelo: 'Kwid', color: null, operacion: 'exportacion' },
        ],
      }),
    );
    expect(await screen.findByText(/Se cargaron 1 VIN\(s\): 1 nuevo\(s\), 0 actualizado\(s\), 1 con error\./)).toBeTruthy();
    expect(screen.getByText('VIN inválido')).toBeTruthy();
  });

  it('un error del API se muestra y no marca la carga como exitosa', async () => {
    const cargarVins = vi.fn().mockReturnValue(throwError(() => new ErrorApi('Datos inválidos', 'VALIDATION_ERROR', 400)));
    await subir(vi.fn().mockResolvedValue([hojaAsignacion]), { cargarVins });

    await userEvent.click(await screen.findByRole('button', { name: /Cargar 2 VINs/ }));

    expect(await screen.findByText('Datos inválidos')).toBeTruthy();
    expect(screen.queryByText(/Se cargaron/)).toBeNull();
  });

  it('bloquea la carga si supera el máximo de filas', async () => {
    const muchas: Hoja = { nombre: 'Grande', filas: [['VIN', 'Marca'], ...Array.from({ length: MAX_FILAS_CARGA + 1 }, (_, i) => [`VIN${i}`, 'Kia'])] };
    await subir(vi.fn().mockResolvedValue([muchas]));

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByRole('button', { name: /Cargar 5001 VINs/ }).hasAttribute('disabled')).toBe(true);
  });

  it('si el archivo no se puede leer muestra el motivo y no avanza', async () => {
    const leer = vi.fn().mockRejectedValue(new ErrorLectura('Formato no compatible. Usa un archivo .xlsx o .csv.'));
    await subir(leer);

    expect(await screen.findByText('Formato no compatible. Usa un archivo .xlsx o .csv.')).toBeTruthy();
    expect(screen.queryByText('2. Hoja y fila de encabezados')).toBeNull();
  });

  it('cambiar la fila de encabezados vuelve a detectar las columnas', async () => {
    const hoja: Hoja = { nombre: 'H', filas: [['Reporte semanal'], ['VIN', 'Marca'], ['VIN00000000000009', 'Kia']] };
    await subir(vi.fn().mockResolvedValue([hoja]));
    expect(await screen.findByText('Falta mapear el VIN y definir la marca.')).toBeTruthy();

    const fila = screen.getByLabelText('Fila de encabezados');
    await userEvent.clear(fila);
    await userEvent.type(fila, '2');

    expect(await screen.findByText('1 listas')).toBeTruthy();
  });
});
