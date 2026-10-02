/** Valor de una celda ya normalizado (las fechas, fórmulas y textos enriquecidos se reducen a texto o número). */
export type Celda = string | number | boolean | null;

export interface Hoja {
  nombre: string;
  /** Filas tal como están en el archivo: el índice 0 es la fila 1 (las filas vacías se conservan como `[]`). */
  filas: Celda[][];
}

export const MAX_BYTES_ARCHIVO = 10 * 1024 * 1024;

export class ErrorLectura extends Error {
  constructor(mensaje: string) {
    super(mensaje);
    this.name = 'ErrorLectura';
  }
}

/** Reduce el valor de una celda de ExcelJS (objeto de fórmula, texto enriquecido, enlace, fecha, error) a un valor simple. */
export function normalizarCelda(valor: unknown): Celda {
  if (valor === null || valor === undefined) return null;
  if (typeof valor === 'string' || typeof valor === 'number' || typeof valor === 'boolean') return valor;
  if (valor instanceof Date) return Number.isNaN(valor.getTime()) ? null : valor.toISOString();
  if (typeof valor === 'object') {
    const o = valor as Record<string, unknown>;
    if ('error' in o) return null;
    if ('result' in o) return normalizarCelda(o['result']);
    if (Array.isArray(o['richText'])) return (o['richText'] as { text?: string }[]).map((t) => t.text ?? '').join('');
    if ('text' in o) return normalizarCelda(o['text']);
  }
  return null;
}

/** Lee un CSV (separador coma o punto y coma, comillas dobles, saltos CRLF o LF). */
export function leerCsv(contenido: string): Celda[][] {
  const texto = contenido.replace(/^﻿/, '');
  const primera = texto.split(/\r?\n/, 1)[0] ?? '';
  const separador = (primera.match(/;/g)?.length ?? 0) > (primera.match(/,/g)?.length ?? 0) ? ';' : ',';

  const filas: Celda[][] = [];
  let fila: Celda[] = [];
  let campo = '';
  let entreComillas = false;

  const cerrarCampo = () => {
    fila.push(campo === '' ? null : campo);
    campo = '';
  };
  const cerrarFila = () => {
    cerrarCampo();
    filas.push(fila.length === 1 && fila[0] === null ? [] : fila);
    fila = [];
  };

  for (let i = 0; i < texto.length; i++) {
    const c = texto[i];
    if (entreComillas) {
      if (c === '"' && texto[i + 1] === '"') {
        campo += '"';
        i++;
      } else if (c === '"') {
        entreComillas = false;
      } else {
        campo += c;
      }
    } else if (c === '"') {
      entreComillas = true;
    } else if (c === separador) {
      cerrarCampo();
    } else if (c === '\n' || c === '\r') {
      if (c === '\r' && texto[i + 1] === '\n') i++;
      cerrarFila();
    } else {
      campo += c;
    }
  }
  if (campo !== '' || fila.length > 0) cerrarFila();
  return filas;
}

async function leerXlsx(buffer: ArrayBuffer): Promise<Hoja[]> {
  // Carga diferida: ExcelJS pesa mucho y solo se necesita al subir un archivo.
  const { Workbook } = await import('exceljs');
  const libro = new Workbook();
  await libro.xlsx.load(buffer);
  return libro.worksheets.map((hoja) => {
    const filas: Celda[][] = Array.from({ length: hoja.rowCount }, () => []);
    hoja.eachRow({ includeEmpty: false }, (fila, numero) => {
      const valores = (fila.values as unknown[]).slice(1);
      filas[numero - 1] = valores.map(normalizarCelda);
    });
    return { nombre: hoja.name, filas };
  });
}

/** Lee un .xlsx o .csv. Se ejecuta en un Web Worker (ver lector-excel.worker.ts) o, sin soporte, en el hilo principal. */
export async function leerArchivo(buffer: ArrayBuffer, nombreArchivo: string): Promise<Hoja[]> {
  const extension = nombreArchivo.split('.').pop()?.toLowerCase();
  try {
    if (extension === 'csv') return [{ nombre: 'CSV', filas: leerCsv(new TextDecoder('utf-8').decode(buffer)) }];
    if (extension === 'xlsx') return await leerXlsx(buffer);
  } catch {
    throw new ErrorLectura('No se pudo leer el archivo. Verifica que sea un Excel o CSV válido.');
  }
  if (extension === 'xls') throw new ErrorLectura('El formato .xls no es compatible. Guarda el archivo como .xlsx o .csv e inténtalo de nuevo.');
  throw new ErrorLectura('Formato no compatible. Usa un archivo .xlsx o .csv.');
}
