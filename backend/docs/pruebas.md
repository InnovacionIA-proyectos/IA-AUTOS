# Pruebas de integración: cómo están armadas y cómo se mantienen fiables

Las pruebas de integración (`backend/tests/IA-AUTOS.Tests.Integracion`) usan un SQL Server 2019 real en un contenedor (Docker Desktop debe estar encendido). No corren en el CI de GitHub (no hay Docker en el
runner): se ejecutan en el equipo antes de abrir el PR. Por eso importan dos cosas: que sean rápidas y que se comporten como la aplicación desplegada.

## Un solo servidor, una base por prueba o colección

`clsServidorSql` levanta **un único contenedor** para toda la ejecución. Cada fixture o prueba crea **su propia base** dentro de él y la borra al terminar, de modo que nada se comparte entre pruebas y pueden
correr en paralelo. Arrancar un contenedor por prueba costaba más que la prueba misma.

- **Pruebas de API (43 clases):** están repartidas en 4 colecciones de xUnit (`clsColecciones.BASE_DE_DATOS_1` a `_4`). Las clases de una colección corren una tras otra sobre su base (que se limpia antes de cada
  prueba con `clsBaseDatosFixture.LimpiarAsync`, igual que antes) y las 4 colecciones corren en paralelo, cada una con su base. Una clase nueva se pone en la colección que menos tarda:
  `[Collection(clsColecciones.BASE_DE_DATOS_n)]`.
- **Pruebas de migración (`Migracion*Tests`):** cada prueba crea su base con `clsServidorSql.CrearBaseAsync("Prefijo")` (y la borra con `EliminarBaseAsync` en `DisposeAsync`), aplica el esquema previo que
  necesita y el script que prueba.

## Mismo comportamiento que en producción

Las pruebas no deben pasar por cosas que fallarían (o se comportarían distinto) en producción:

- **Toda base se crea con `database/00-crear-base.sql`, el script que usa producción** (colación `Modern_Spanish_CI_AS`, nivel de compatibilidad 150 y `READ_COMMITTED_SNAPSHOT`). Antes las pruebas de
  migración usaban los valores por defecto del servidor y escondían errores: el script 13 comparaba una tabla temporal (colación de `tempdb`) con columnas de la base y fallaba con la colación de producción.
  Ya está corregido; en un script nuevo, las tablas temporales y las variables de tabla con texto llevan `COLLATE DATABASE_DEFAULT`.
- **La imagen es la misma versión de SQL Server que producción** (2019) y no se reutiliza entre ejecuciones (sin estado heredado): el contenedor se destruye al terminar.
- **La limpieza entre pruebas no se acorta:** se sigue borrando todo y devolviendo los catálogos y parámetros a su valor inicial. Lo que se paralelizó fue el reparto entre bases, no el aislamiento.
- **La API arranca en memoria con su configuración propia por prueba** (`clsFabricaApi`: cadena de conexión, secretos y carpeta de archivos por instancia; ninguna variable de entorno compartida), así que
  correr colecciones en paralelo no mezcla configuraciones.

## La migración encadenada (`MigracionEncadenadaTests`)

Es el camino real de una base ya desplegada: parte del esquema **más antiguo que se conserva** (`Esquemas/ddl-previo-a-viajes.sql`) con datos de negocio, aplica **todos** los scripts de
`database/cambios-esquema` en orden, tal como se entregan, y comprueba:

1. los scripts se aplicaron todos y en orden, sin huecos en la numeración;
2. los datos de antes siguen ahí;
3. el esquema resultante es **idéntico** al de una base nueva creada con `ddl.sql`: tablas, columnas (tipo, largo, nulabilidad, colación), valores por defecto, restricciones, llaves foráneas, índices,
   triggers, vistas y la configuración de la base. Si una migración y el `ddl.sql` dejan algo distinto, la prueba lista exactamente qué;
4. repetir todos los scripts y el `ddl.sql` sobre esa base no cambia el esquema ni los datos.

Los scripts y los esquemas previos entran por comodín en el `.csproj` (`database/cambios-esquema/*.sql`, `Esquemas/*.sql`): **un script nuevo entra solo a la cadena**, sin registrarlo. Las pruebas
`Migracion*Tests` por script se conservan para lo que es específico de ese script (datos transformados, restricciones, caso de repetición); ya no hace falta que cada una repita lo que cubre la encadenada.

## Cuando se agrega un script de esquema

1. Crear `database/cambios-esquema/NN-nombre.sql` (ASCII, `@esquema`, una transacción, repetible) y dejar `ddl.sql` al día.
2. Guardar el esquema previo: `git show HEAD:backend/database/ddl.sql > tests/IA-AUTOS.Tests.Integracion/Infraestructura/Esquemas/ddl-previo-a-<nombre>.sql`.
3. Escribir su `Migracion<Nombre>Tests` solo con lo específico y ejecutar la encadenada: debe seguir en verde.

## Ejecutarlas

```
# todo, en dos tandas seguidas (API y demás, luego migraciones); es lo que conviene antes de abrir un PR
pwsh backend/tests/probar-integracion.ps1
# solo lo que se toca
dotnet test backend/tests/IA-AUTOS.Tests.Integracion --no-build --filter "FullyQualifiedName~AcomodacionApiTests"
# solo migraciones
dotnet test backend/tests/IA-AUTOS.Tests.Integracion --no-build --filter "FullyQualifiedName~Infraestructura.Migracion"
```

**Por qué en dos tandas:** las dos juntas en un solo `dotnet test` compiten por el mismo SQL Server y tardan más que la suma de las dos por separado (medido: 16 a 21 min juntas frente a unos 11 a 17 min en dos tandas).

## Tiempos medidos (equipo de desarrollo de 12 núcleos, Docker Desktop)

| | Antes | Ahora |
|---|---|---|
| Pruebas de API y demás (962) | unos 23 min | de 7,5 a 13 min (varía con la carga del equipo) |
| Pruebas de migración (121) | unos 9,5 min | de 3 a 3,5 min |
| Todo | unos 27 a 33 min | de 11 a 17 min |

Lo que se probó y no ayudó (queda anotado para no repetirlo): **más colecciones** (con 6 tardó más que con 4: el SQL Server se satura); **limitar los hilos de xUnit** (la suite completa subió a 21 min);
**guardar cada esquema previo como respaldo y restaurar una copia por prueba** (mismo tiempo: el costo está en aplicar el DDL varias veces por prueba, no en crear la base). El reparto de las clases en las 4 colecciones
se hizo con tiempos medidos (`--logger trx`): cada colección tarda lo mismo (unos 280 s de pruebas).

Para volver a medir o rebalancear: `dotnet test ... --filter "FullyQualifiedName!~Infraestructura.Migracion" --logger "trx;LogFileName=par.trx"` y sumar la duración por clase.
