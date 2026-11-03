"""Incrusta el archivo del dibujo de danos (regiones-zonas-dano.json) en el guion de resiembra.

Uso:  python generar-resiembra-dibujo.py <archivo.json> <guion.sql>

Reemplaza lo que hay entre las lineas "-- JSON-INICIO" y "-- JSON-FIN" del guion por el contenido del archivo, compacto y en ASCII (los acentos como
\\uXXXX), partido en sentencias cortas para que cualquier cliente SQL lo abra. Un dibujo nuevo es un archivo nuevo: se vuelve a correr este programa y se
ejecuta el guion; el guion solo resiembra si la version del archivo no es la que ya esta en la base.
"""
import json
import re
import sys

LONGITUD_TROZO = 3000


def trozos(texto: str):
    # No parte una secuencia \uXXXX ni un par de comillas.
    unidades = re.findall(r"\\u[0-9a-fA-F]{4}|''|\\.|.", texto, flags=re.DOTALL)
    actual = ""
    for unidad in unidades:
        if len(actual) + len(unidad) > LONGITUD_TROZO:
            yield actual
            actual = ""
        actual += unidad
    if actual:
        yield actual


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 1
    with open(sys.argv[1], encoding="utf-8") as f:
        dibujo = json.load(f)
    compacto = json.dumps(dibujo, ensure_ascii=True, separators=(",", ":")).replace("'", "''")
    lineas = [f"SET @json += N'{trozo}';" for trozo in trozos(compacto)]
    with open(sys.argv[2], "rb") as f:
        guion = f.read().decode("utf-8").replace("\r\n", "\n")
    patron = re.compile(r"(-- JSON-INICIO\n).*?(-- JSON-FIN)", flags=re.DOTALL)
    if not patron.search(guion):
        print("El guion no tiene las marcas -- JSON-INICIO y -- JSON-FIN")
        return 1
    nuevo = patron.sub(lambda m: m.group(1) + "\n".join(lineas) + "\n" + m.group(2), guion)
    with open(sys.argv[2], "wb") as f:
        f.write(nuevo.replace("\n", "\r\n").encode("utf-8"))
    print(f"version {dibujo.get('version')}: {len(dibujo['paneles'])} paneles, {len(dibujo['cuadrantes'])} cuadrantes, {len(dibujo['regiones'])} regiones, {len(lineas)} sentencias")
    return 0


if __name__ == "__main__":
    sys.exit(main())
