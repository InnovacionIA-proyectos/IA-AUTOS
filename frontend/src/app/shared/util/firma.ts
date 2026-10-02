/** Calcula un viewBox ajustado al trazo de la firma (path SVG que genera la app móvil). */
export function viewBoxDeFirma(trazo: string): string {
  const numeros = trazo.match(/-?\d+(?:\.\d+)?/g)?.map(Number) ?? [];
  if (numeros.length < 4) return '0 0 340 160';
  const xs = numeros.filter((_, i) => i % 2 === 0);
  const ys = numeros.filter((_, i) => i % 2 === 1);
  const margen = 12;
  const minX = Math.min(...xs) - margen;
  const minY = Math.min(...ys) - margen;
  const ancho = Math.max(...xs) - minX + margen;
  const alto = Math.max(...ys) - minY + margen;
  return `${minX} ${minY} ${Math.max(ancho, 40)} ${Math.max(alto, 40)}`;
}

/** Una firma dibujada es un trazo SVG (empieza con "M"); cualquier otro texto se trata como un identificador heredado. */
export function esTrazoSvg(valor: string | null | undefined): boolean {
  return !!valor?.trim().startsWith('M');
}
