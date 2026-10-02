/** Formatea una fecha ISO a una cadena legible en español (es-CO). */
export function formatearFecha(iso: unknown): string {
  if (typeof iso !== 'string' || !iso) return '—';
  const fecha = new Date(iso);
  return Number.isNaN(fecha.getTime()) ? iso : fecha.toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' });
}

/** Reemplaza guiones bajos por espacios y capitaliza (para valores de catálogo legibles). */
export function humanizar(valor: string | null | undefined): string {
  if (!valor) return '';
  const texto = valor.replace(/_/g, ' ');
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}
