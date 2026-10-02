import { humanizar } from './formato';

/** Etiquetas legibles de las etapas (puntos de custodia). */
export const ETIQUETA_PROCESO: Record<string, string> = {
  cargue: 'Cargue',
  transporte: 'Transporte',
  descargue: 'Descargue',
  punto_origen: 'Punto de origen',
  certificadora: 'Certificadora',
  punto_destino: 'Punto de destino',
  concesionario: 'Concesionario',
};

/**
 * Momento del informe. "informe" es el flujo actual (un informe por etapa); check_in y check_out se conservan
 * solo para los informes históricos.
 */
export const ETIQUETA_MOMENTO: Record<string, string> = {
  informe: 'Informe',
  check_in: 'Check-in',
  check_out: 'Check-out',
};

export const ETIQUETA_ESTADO: Record<string, string> = {
  cerrada: 'Cerrada',
  borrador: 'Borrador',
};

export const etiquetaProceso = (valor: string): string => ETIQUETA_PROCESO[valor] ?? humanizar(valor);
export const etiquetaMomento = (valor: string): string => ETIQUETA_MOMENTO[valor] ?? humanizar(valor);
export const etiquetaEstado = (valor: string): string => ETIQUETA_ESTADO[valor] ?? humanizar(valor);
