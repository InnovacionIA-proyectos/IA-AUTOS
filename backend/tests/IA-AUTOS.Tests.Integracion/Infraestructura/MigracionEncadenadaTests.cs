using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// El camino real de una base ya desplegada hasta hoy, en una sola prueba: parte del esquema MAS ANTIGUO que se conserva (Esquemas/ddl-previo-a-viajes.sql) con datos de negocio, aplica TODOS los scripts de
/// <c>database/cambios-esquema</c> en orden (cada uno tal como se entrega, sin atajos) y comprueba tres cosas: los datos de antes siguen ahi; el esquema resultante es el mismo que el de una base nueva creada con
/// <c>ddl.sql</c> (tablas, columnas, valores por defecto, restricciones, llaves foraneas, indices, triggers, vistas y los catalogos sembrados), y repetir todos los scripts y el <c>ddl.sql</c> sobre esa base no cambia nada.
/// Los scripts se descubren en la carpeta: uno nuevo entra solo a la cadena. Las bases se crean con la configuracion de produccion (<see cref="clsServidorSql"/>).
/// </summary>
public sealed partial class MigracionEncadenadaTests(MigracionEncadenadaTests.clsBases bases) : IClassFixture<MigracionEncadenadaTests.clsBases>
{
    /// <summary>Las dos bases que se comparan, armadas una sola vez: la migrada paso a paso y la nueva con el ddl.</summary>
    public sealed class clsBases : IAsyncLifetime
    {
        public string Migrada { get; private set; } = string.Empty;

        public string Nueva { get; private set; } = string.Empty;

        /// <summary>Los scripts aplicados, en orden.</summary>
        public IReadOnlyList<string> Scripts { get; private set; } = [];

        public Dictionary<string, int> FilasAntes { get; } = [];

        public Dictionary<string, int> FilasDespuesDeMigrar { get; } = [];

        public async Task InitializeAsync()
        {
            Migrada = await clsServidorSql.CrearBaseAsync("MigradaPasoAPaso");
            Nueva = await clsServidorSql.CrearBaseAsync("NuevaConDdl");
            var raiz = AppContext.BaseDirectory;
            await clsServidorSql.EjecutarScriptAsync(Migrada, Path.Combine(raiz, "Infraestructura", "Esquemas", "ddl-previo-a-viajes.sql"));
            await EjecutarAsync(Migrada, MigracionViajesTests.DATOS_PREVIOS);
            foreach (var tabla in TABLAS_CON_DATOS)
            {
                FilasAntes[tabla] = await ContarAsync(Migrada, $"SELECT COUNT(*) FROM dbo.{tabla}");
            }

            // Cada script se aplica tal como se entrega; solo se cambia el esquema de destino (db_IA_AUTOS en el script, dbo en estas bases).
            var scripts = Directory.GetFiles(Path.Combine(raiz, "database", "cambios-esquema"), "*.sql")
                .Select(Path.GetFileName).OfType<string>().Where(n => int.Parse(n[..2]) >= PRIMER_SCRIPT).Order(StringComparer.Ordinal).ToList();
            Scripts = scripts;
            foreach (var nombre in scripts)
            {
                await EjecutarAsync(Migrada, LeerScript(nombre));
            }

            foreach (var tabla in TABLAS_CON_DATOS)
            {
                FilasDespuesDeMigrar[tabla] = await ContarAsync(Migrada, $"SELECT COUNT(*) FROM dbo.{tabla}");
            }

            await clsServidorSql.EjecutarScriptAsync(Nueva, Path.Combine(raiz, "database", "ddl.sql"));
        }

        public async Task DisposeAsync()
        {
            await clsServidorSql.EliminarBaseAsync(Migrada);
            await clsServidorSql.EliminarBaseAsync(Nueva);
        }
    }

    // El esquema previo mas antiguo que se conserva ya trae los cambios 01 a 04: la cadena empieza en el 05.
    private const int PRIMER_SCRIPT = 5;

    private static readonly string[] TABLAS_CON_DATOS = ["tblVehiculos", "tblEtapas", "tblInspecciones", "tblUsuarios", "tblOrdenesSofasa"];

    private static string LeerScript(string nombre) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", nombre)).Replace("N'db_IA_AUTOS'", "N'dbo'");

    private static async Task EjecutarAsync(string cadena, string sql)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion);
        await preparar.ExecuteNonQueryAsync();
        await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 300 };
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<int> ContarAsync(string cadena, string sql)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        return Convert.ToInt32(await comando.ExecuteScalarAsync());
    }

    /// <summary>
    /// Una linea de texto por cada objeto del esquema, sin depender del orden ni de los espacios: lo que se compara entre dos bases. Cada consulta devuelve sus datos en columnas SEPARADAS y la linea
    /// se arma aqui: unir en SQL los nombres del catalogo del sistema (otra colacion) con textos de la base fallaria con la colacion de produccion. La columna <c>definicion</c> se compara normalizada.
    /// </summary>
    internal static async Task<HashSet<string>> RadiografiaAsync(string cadena)
    {
        var lineas = new HashSet<string>(StringComparer.Ordinal);
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        foreach (var (tipo, sql) in CONSULTAS_DEL_ESQUEMA)
        {
            await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 180 };
            try
            {
                await using var lector = await comando.ExecuteReaderAsync();
                while (await lector.ReadAsync())
                {
                    var partes = new List<string> { tipo };
                    for (var i = 0; i < lector.FieldCount; i++)
                    {
                        var valor = lector.IsDBNull(i) ? "-" : Convert.ToString(lector.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "-";
                        // Las definiciones (restricciones, triggers, vistas, filtros) se comparan sin espacios, corchetes ni mayusculas: lo que cambia entre escribirlas a mano o por un script.
                        partes.Add(lector.GetName(i) == "definicion" ? Normalizada(valor) : $"{lector.GetName(i)}={valor}");
                    }

                    lineas.Add(string.Join(" | ", partes));
                }
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException("Fallo la consulta de la radiografia del esquema: " + sql, ex);
            }
        }

        return lineas;
    }

    /// <summary>La definicion sin comentarios, espacios, corchetes ni mayusculas: un comentario antes o despues del CREATE no cambia lo que hace el objeto.</summary>
    private static string Normalizada(string definicion) =>
        EspaciosYCorchetes().Replace(ComentariosSql().Replace(definicion, string.Empty), string.Empty).ToLowerInvariant();

    [GeneratedRegex(@"[\s\[\]]+")]
    private static partial Regex EspaciosYCorchetes();

    [GeneratedRegex(@"--[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex ComentariosSql();

    private static readonly (string Tipo, string Sql)[] CONSULTAS_DEL_ESQUEMA =
    [
        // tablas y columnas: tipo, largo, precision, nulabilidad, identidad, colacion y columna calculada
        ("COLUMNA", """
            SELECT t.name AS tabla, c.name AS columna, ty.name AS tipo, c.max_length AS largo, c.precision AS precision_, c.scale AS escala, c.is_nullable AS nulable, c.is_identity AS identidad,
                   c.collation_name AS colacion, cc.definition AS definicion
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            WHERE t.is_ms_shipped = 0
            """),
        ("TABLA", "SELECT t.name AS tabla FROM sys.tables t WHERE t.is_ms_shipped = 0"),
        // valores por defecto
        ("DEFAULT", """
            SELECT t.name AS tabla, c.name AS columna, d.name AS nombre, d.definition AS definicion
            FROM sys.default_constraints d
            JOIN sys.tables t ON t.object_id = d.parent_object_id
            JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
            WHERE t.is_ms_shipped = 0
            """),
        // restricciones CHECK (con si estan activas)
        ("CHECK", """
            SELECT t.name AS tabla, k.name AS nombre, k.is_disabled AS desactivada, k.definition AS definicion
            FROM sys.check_constraints k JOIN sys.tables t ON t.object_id = k.parent_object_id WHERE t.is_ms_shipped = 0
            """),
        // llaves foraneas: columnas, tabla referenciada y acciones
        ("FK", """
            SELECT f.name AS nombre, tp.name AS tabla, tr.name AS referencia, f.delete_referential_action_desc AS borrar, f.update_referential_action_desc AS actualizar, f.is_disabled AS desactivada,
                   (SELECT STRING_AGG(cp.name, ',') WITHIN GROUP (ORDER BY fc.constraint_column_id)
                    FROM sys.foreign_key_columns fc JOIN sys.columns cp ON cp.object_id = fc.parent_object_id AND cp.column_id = fc.parent_column_id WHERE fc.constraint_object_id = f.object_id) AS columnas,
                   (SELECT STRING_AGG(cr.name, ',') WITHIN GROUP (ORDER BY fc.constraint_column_id)
                    FROM sys.foreign_key_columns fc JOIN sys.columns cr ON cr.object_id = fc.referenced_object_id AND cr.column_id = fc.referenced_column_id WHERE fc.constraint_object_id = f.object_id) AS columnas_ref
            FROM sys.foreign_keys f JOIN sys.tables tp ON tp.object_id = f.parent_object_id JOIN sys.tables tr ON tr.object_id = f.referenced_object_id
            WHERE tp.is_ms_shipped = 0
            """),
        // indices (y llaves primarias y unicas): columnas en orden, direccion, incluidas y filtro
        ("INDICE", """
            SELECT t.name AS tabla, i.name AS nombre, i.type_desc AS tipo, i.is_unique AS unico, i.is_primary_key AS pk, i.is_unique_constraint AS uq, i.is_disabled AS desactivado,
                   ISNULL((SELECT STRING_AGG(c.name + CASE WHEN ic.is_descending_key = 1 THEN ' desc' ELSE '' END, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                           FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0), '') AS claves,
                   ISNULL((SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY c.name)
                           FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1), '') AS incluidas,
                   i.filter_definition AS definicion
            FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE t.is_ms_shipped = 0 AND i.type > 0
            """),
        // triggers, vistas, procedimientos y funciones: su definicion completa
        ("TRIGGER", """
            SELECT o.name AS nombre, OBJECT_NAME(o.parent_object_id) AS tabla, tr.is_disabled AS desactivado, m.definition AS definicion
            FROM sys.triggers tr JOIN sys.objects o ON o.object_id = tr.object_id JOIN sys.sql_modules m ON m.object_id = tr.object_id WHERE tr.is_ms_shipped = 0
            """),
        ("OBJETO", """
            SELECT o.type_desc AS tipo, o.name AS nombre, m.definition AS definicion
            FROM sys.objects o JOIN sys.sql_modules m ON m.object_id = o.object_id WHERE o.is_ms_shipped = 0 AND o.type IN ('V', 'P', 'FN', 'IF', 'TF')
            """),
        // la configuracion de la base que cambia el comportamiento (la misma que produccion)
        ("BASE", "SELECT d.collation_name AS colacion, d.compatibility_level AS compatibilidad, d.is_read_committed_snapshot_on AS rcsi FROM sys.databases d WHERE d.database_id = DB_ID()"),
    ];

    [Fact]
    public void Todos_los_scripts_de_la_carpeta_se_aplicaron_en_orden_sobre_el_esquema_mas_antiguo()
    {
        Assert.NotEmpty(bases.Scripts);
        Assert.Equal(bases.Scripts.Order(StringComparer.Ordinal).ToList(), bases.Scripts);
        Assert.Equal("05", bases.Scripts[0][..2]);
        // Sin huecos en la numeracion: un script que falte en la carpeta rompe la cadena de las bases desplegadas.
        Assert.Equal(Enumerable.Range(PRIMER_SCRIPT, bases.Scripts.Count).Select(n => n.ToString("00")).ToList(), bases.Scripts.Select(s => s[..2]).ToList());
    }

    [Fact]
    public void Los_datos_de_antes_siguen_ahi_despues_de_toda_la_cadena()
    {
        foreach (var tabla in TABLAS_CON_DATOS)
        {
            Assert.True(bases.FilasAntes[tabla] > 0, $"La base previa debia traer filas en {tabla}");
        }

        Assert.Equal(bases.FilasAntes["tblVehiculos"], bases.FilasDespuesDeMigrar["tblVehiculos"]);
        Assert.Equal(bases.FilasAntes["tblInspecciones"], bases.FilasDespuesDeMigrar["tblInspecciones"]);
        Assert.Equal(bases.FilasAntes["tblUsuarios"], bases.FilasDespuesDeMigrar["tblUsuarios"]);
        Assert.Equal(bases.FilasAntes["tblOrdenesSofasa"], bases.FilasDespuesDeMigrar["tblOrdenesSofasa"]);
    }

    [Fact]
    public async Task El_esquema_migrado_paso_a_paso_es_el_mismo_que_el_de_una_base_nueva_con_ddl_sql()
    {
        var migrada = await RadiografiaAsync(bases.Migrada);
        var nueva = await RadiografiaAsync(bases.Nueva);

        var soloEnMigrada = migrada.Except(nueva).Order(StringComparer.Ordinal).ToList();
        var soloEnNueva = nueva.Except(migrada).Order(StringComparer.Ordinal).ToList();

        Assert.True(soloEnMigrada.Count == 0 && soloEnNueva.Count == 0,
            "El esquema migrado difiere del de ddl.sql.\n--- solo en la base migrada:\n" + string.Join('\n', soloEnMigrada) + "\n--- solo en la base nueva:\n" + string.Join('\n', soloEnNueva));
    }

    [Fact]
    public async Task Repetir_todos_los_scripts_y_el_ddl_sobre_la_base_migrada_no_cambia_el_esquema_ni_los_datos()
    {
        // Se trabaja sobre una copia de la ejecucion: la base migrada se deja intacta para las demas pruebas de la clase.
        var otra = await clsServidorSql.CrearBaseAsync("RepetidaVeces");
        try
        {
            await clsServidorSql.EjecutarScriptAsync(otra, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-viajes.sql"));
            await EjecutarAsync(otra, MigracionViajesTests.DATOS_PREVIOS);
            foreach (var nombre in bases.Scripts)
            {
                await EjecutarAsync(otra, LeerScript(nombre));
            }

            var despuesDeLaPrimera = await RadiografiaAsync(otra);
            var filasPrimera = new Dictionary<string, int>();
            foreach (var tabla in TABLAS_CON_DATOS)
            {
                filasPrimera[tabla] = await ContarAsync(otra, $"SELECT COUNT(*) FROM dbo.{tabla}");
            }

            // Segunda vuelta completa y despues el ddl.sql encima de la base ya migrada.
            foreach (var nombre in bases.Scripts)
            {
                await EjecutarAsync(otra, LeerScript(nombre));
            }

            await clsServidorSql.EjecutarScriptAsync(otra, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

            var despuesDeRepetir = await RadiografiaAsync(otra);
            Assert.True(despuesDeLaPrimera.SetEquals(despuesDeRepetir),
                "Repetir los scripts y el ddl cambio el esquema.\n--- antes:\n" + string.Join('\n', despuesDeLaPrimera.Except(despuesDeRepetir)) + "\n--- despues:\n" + string.Join('\n', despuesDeRepetir.Except(despuesDeLaPrimera)));
            foreach (var tabla in TABLAS_CON_DATOS)
            {
                Assert.Equal(filasPrimera[tabla], await ContarAsync(otra, $"SELECT COUNT(*) FROM dbo.{tabla}"));
            }
        }
        finally
        {
            await clsServidorSql.EliminarBaseAsync(otra);
        }
    }
}
