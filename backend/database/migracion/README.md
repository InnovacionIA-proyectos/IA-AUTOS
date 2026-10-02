# Migracion de datos: PostgreSQL a SQL Server 2019

Esta carpeta contiene lo necesario para trasladar los datos del sistema actual (PostgreSQL 16) a la base `IAAutos` en SQL Server 2019. Es una carga inicial unica, no una replicacion.

Si los datos productivos ya estan en SQL Server, o si se arranca con una base vacia, no se necesita nada de esta carpeta: basta con `database/ddl.sql`.

## Archivos

| Archivo | Donde se ejecuta | Para que |
|---|---|---|
| `00-preflight-postgres.sql` | origen (psql) | Detecta datos que bloquearian o ensuciarian la carga. Solo lectura. |
| `01-generar-inserts-postgres.sql` | origen (psql) | Genera `datos.sql`, un script T-SQL con todos los datos. |
| `reconciliar-postgres.sql` | origen (psql) | Indicadores de conciliacion del origen. |
| `reconciliar-sqlserver.sql` | destino (sqlcmd) | Los mismos indicadores en el destino. |

## Procedimiento

Requisitos: `psql` con acceso de lectura al origen y `sqlcmd` con acceso a la base destino. Las cadenas de conexion y contrasenas se pasan por variables de entorno o parametros; no se escriben en estos archivos.

1. Crear el esquema destino (solo si la base no existe):

   ```
   sqlcmd -S <servidor> -U <usuario_admin> -P <password> -I -b -i ../00-crear-base.sql
   sqlcmd -S <servidor> -d IAAutos -U <usuario_admin> -P <password> -I -b -i ../ddl.sql
   ```

2. Verificacion previa en el origen. Cada consulta debe devolver 0 filas (el punto 6 es informativo):

   ```
   psql "<cadena_origen>" -X -v ON_ERROR_STOP=1 -f 00-preflight-postgres.sql
   ```

3. Generar el script de datos. Use `reset_passwords=1` si algun usuario conserva la contrasena de desarrollo de la migracion `20260626130000_auth_password` (el preflight lo sugiere cuando varios usuarios comparten el mismo hash):

   ```
   psql "<cadena_origen>" -X -q -v ON_ERROR_STOP=1 [-v reset_passwords=1] -f 01-generar-inserts-postgres.sql -o ../export/datos.sql
   ```

4. Cargar en el destino. Debe ser una base sin datos de negocio: el script aborta con el error 53000 si ya hay usuarios, vehiculos o etapas. Se usa `-f 65001` porque el archivo esta en UTF-8:

   ```
   sqlcmd -S <servidor> -d IAAutos -U <usuario_admin> -P <password> -I -b -f 65001 -i ../export/datos.sql
   ```

   Durante la carga se deshabilitan los triggers de inmutabilidad (permiten insertar informes historicos ya cerrados) y se rehabilitan al terminar. El catalogo de puntos de custodia se reemplaza conservando los identificadores originales.

5. Reconciliar. No debe haber diferencias:

   ```
   psql "<cadena_origen>" -X -q -A -t -F '|' -v ON_ERROR_STOP=1 -f reconciliar-postgres.sql > ../export/origen.txt
   sqlcmd -S <servidor> -d IAAutos -U <usuario> -P <password> -I -b -h -1 -W -i reconciliar-sqlserver.sql -o ../export/destino.txt
   diff ../export/origen.txt ../export/destino.txt
   ```

6. Borrar `database/export/`: contiene datos de personas y hashes de contrasenas.

## Que se verifica en la conciliacion

Conteos de todas las tablas, distribuciones por operacion, momento y clase de dano, inspecciones cerradas, evidencias geolocalizadas, transferencias completas, sumas de control de latitud y peso, y las unidades movilizadas y con novedad. Estas dos ultimas se calculan con la misma regla en ambos motores: en el origen con la consulta original y en el destino con la vista `vwVehiculosMovilizados`.

## Notas

- Las contrasenas se migran tal cual (`scrypt:<sal>:<hash>`). La API nueva verifica ese formato y lo actualiza al formato nuevo en el siguiente inicio de sesion del usuario.
- Los correos se normalizan a minusculas. El destino distingue acentos pero no mayusculas.
- El destino permite una sola transferencia de custodia por inspeccion y numeros de VIN unicos sin distinguir mayusculas; el preflight avisa si el origen no cumple.
- Estado de ensayo: este procedimiento se ejecuto de extremo a extremo con PostgreSQL 16 (esquema y semillas originales) hacia SQL Server 2019 CU32, con 38 indicadores identicos.
- La carga no es reanudable: si falla a medias, recree la base destino (`ddl.sql` sobre una base nueva) y repita desde el paso 4.
