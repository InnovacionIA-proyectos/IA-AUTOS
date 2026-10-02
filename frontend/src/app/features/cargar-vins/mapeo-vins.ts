import type { Operacion } from '../../core/api/api-tipos';
import type { Celda } from './lector-excel';

/** El API acepta como máximo esta cantidad de filas por solicitud (VehiculosValidators.MAXIMO_FILAS). */
export const MAX_FILAS_CARGA = 5000;

export const ETIQUETA_OPERACION: Record<Operacion, string> = {
  importacion: 'Importación',
  exportacion: 'Exportación',
  nacional: 'Nacional',
};

/** Índices de columna (como texto) o '' cuando no está asignada. */
export interface Mapeo {
  vin: string;
  marca: string;
  modelo: string;
  color: string;
}

export interface FilaRevisada {
  /** Número de fila en el archivo original. */
  fila: number;
  vin: string;
  marca: string;
  modelo: string;
  color: string;
  faltaVin: boolean;
  faltaMarca: boolean;
  lista: boolean;
}

export const texto = (celda: Celda | undefined): string => (celda === null || celda === undefined ? '' : String(celda).trim());

/** Letra de columna al estilo Excel: 0 -> A, 25 -> Z, 26 -> AA. */
export function letraColumna(indice: number): string {
  let letra = '';
  for (let n = indice + 1; n > 0; n = Math.floor((n - 1) / 26)) letra = String.fromCharCode(65 + ((n - 1) % 26)) + letra;
  return letra;
}

function detectar(encabezados: string[], patron: RegExp): string {
  const indice = encabezados.findIndex((h) => patron.test(h));
  return indice >= 0 ? String(indice) : '';
}

/** Adivina qué columna es cada dato a partir de los encabezados (a veces el VIN llega como "CHASIS"). */
export function autoMapear(encabezados: string[]): Mapeo {
  return {
    vin: detectar(encabezados, /vin|chasis|chassis/i),
    marca: detectar(encabezados, /marca|brand/i),
    modelo: detectar(encabezados, /modelo|descrip|model/i),
    color: detectar(encabezados, /color/i),
  };
}

export function encabezadosDe(filas: readonly Celda[][], filaEncabezado: number): string[] {
  const fila = filas[filaEncabezado - 1] ?? [];
  const columnas = filas.reduce((max, f) => Math.max(max, f.length), fila.length);
  return Array.from({ length: columnas }, (_, i) => texto(fila[i]));
}

/** Aplica el mapeo a las filas de datos (las siguientes al encabezado) y valida VIN y marca. Ignora filas totalmente vacías. */
export function revisarFilas(filas: readonly Celda[][], filaEncabezado: number, mapeo: Mapeo, marcaFija: string): FilaRevisada[] {
  const indice = (valor: string) => (valor === '' ? -1 : Number(valor));
  const [iVin, iMarca, iModelo, iColor] = [mapeo.vin, mapeo.marca, mapeo.modelo, mapeo.color].map(indice);

  return filas
    .map((celdas, posicion) => ({ celdas, fila: posicion + 1 }))
    .filter(({ fila, celdas }) => fila > filaEncabezado && celdas.some((c) => texto(c) !== ''))
    .map(({ celdas, fila }) => {
      const vin = iVin >= 0 ? texto(celdas[iVin]) : '';
      const marca = iMarca >= 0 ? texto(celdas[iMarca]) : marcaFija.trim();
      const faltaVin = !vin;
      const faltaMarca = !marca;
      return {
        fila,
        vin,
        marca,
        modelo: iModelo >= 0 ? texto(celdas[iModelo]) : '',
        color: iColor >= 0 ? texto(celdas[iColor]) : '',
        faltaVin,
        faltaMarca,
        lista: !faltaVin && !faltaMarca,
      };
    });
}
