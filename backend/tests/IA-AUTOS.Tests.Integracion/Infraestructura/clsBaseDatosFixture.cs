using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Levanta un SQL Server 2019 real en un contenedor y aplica los scripts del proyecto
/// (database/00-crear-base.sql y database/ddl.sql). Se comparte entre todas las pruebas de la coleccion.
/// </summary>
public sealed partial class clsBaseDatosFixture : IAsyncLifetime
{
    private const string IMAGEN = "mcr.microsoft.com/mssql/server:2019-latest";
    private const string BASE = "IAAutos";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder(IMAGEN).Build();

    /// <summary>Cadena de conexion a la base IAAutos del contenedor.</summary>
    public string CadenaConexion { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();

        var maestra = _contenedor.GetConnectionString();
        await EjecutarScriptAsync(maestra, "00-crear-base.sql");

        var constructor = new SqlConnectionStringBuilder(maestra) { InitialCatalog = BASE };
        CadenaConexion = constructor.ConnectionString;
        await EjecutarScriptAsync(CadenaConexion, "ddl.sql");
    }

    public async Task DisposeAsync() => await _contenedor.DisposeAsync();

    /// <summary>
    /// Borra todos los datos de negocio (conserva los catalogos). La base protege la evidencia con triggers de no borrado: se apagan solo
    /// mientras dura la limpieza y se vuelven a encender siempre, aunque falle.
    /// </summary>
    public async Task LimpiarAsync()
    {
        const string SQL = """
            BEGIN TRY
            ALTER TABLE dbo.tblTransferenciasCustodia DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblEvidencias DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblAuditLog DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgoSeguimiento DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL;
            DELETE FROM dbo.tblNotificaciones;
            DELETE FROM dbo.tblDispositivos;
            DELETE FROM dbo.tblHallazgoSeguimiento;
            DELETE FROM dbo.tblRefreshTokens;
            DELETE FROM dbo.tblAuditLog;
            DELETE FROM dbo.tblOrdenesSofasa;
            DELETE FROM dbo.tblTransferenciasCustodia;
            DELETE FROM dbo.tblEvidencias;
            DELETE FROM dbo.tblViajeVehiculos;
            DELETE FROM dbo.tblHallazgos;
            DELETE FROM dbo.tblInspecciones;
            DELETE FROM dbo.tblEtapas;
            DELETE FROM dbo.tblViajes;
            DELETE FROM dbo.tblVehiculos;
            DELETE FROM dbo.tblUsuarios;
            ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblAuditLog ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgoSeguimiento ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL;
            END TRY
            BEGIN CATCH
            ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblAuditLog ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgoSeguimiento ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL;
            THROW;
            END CATCH

            -- Catalogos: vuelven a su estado inicial (sin valores creados por las pruebas, todos activos y sin cambios de forma).
            DELETE FROM dbo.tblCuadrantes;
            DELETE FROM dbo.tblEtapasCatalogo WHERE Codigo NOT IN ('cargue', 'transporte', 'descargue');
            DELETE FROM dbo.tblPuntosCustodia WHERE Codigo NOT IN ('cedi_1', 'origen', 'certificadora', 'destino', 'cedi_2', 'concesionario');
            DELETE FROM dbo.tblClientes WHERE Codigo <> 'sofasa';
            DELETE FROM dbo.tblTiposUnidadTransportadora WHERE Codigo NOT IN ('ninera', 'nodriza', 'quintinaria', 'planchon_grua');
            DELETE FROM dbo.tblClasesDano WHERE Codigo NOT IN ('deformacion', 'rotura', 'cortado', 'perforacion', 'golpe', 'mancha', 'raya_pintar', 'desconchado', 'particulas', 'faltante');
            DELETE FROM dbo.tblTamanosDano WHERE Codigo NOT IN ('1_5cm', '5_10cm', '10_20cm', 'mas_20cm', 'requiere_reemplazo', 'mas_10_particulas');
            DELETE FROM dbo.tblNivelesSeveridad WHERE Codigo NOT IN ('v1_plus', 'v1', 'v2', 'v3');
            DELETE FROM dbo.tblVistasFoto WHERE Codigo NOT IN ('frontal', 'trasera', 'lateral_derecha', 'lateral_izquierda', 'diagonal_trasera_izquierda', 'diagonal_delantera_derecha', 'superior', 'piezas_baul', 'cojineria');
            UPDATE dbo.tblEtapasCatalogo SET Activo = 1;
            UPDATE dbo.tblPuntosCustodia SET Activo = 1;
            UPDATE dbo.tblTiposUnidadTransportadora SET Activo = 1;
            UPDATE dbo.tblClientes SET Activo = 1, Orden = 1;
            UPDATE dbo.tblClasesDano SET Activo = 1;
            UPDATE dbo.tblTamanosDano SET Activo = 1;
            UPDATE dbo.tblNivelesSeveridad SET Activo = 1, Accion = CASE Codigo
                WHEN 'v1_plus' THEN N'Se recupera antes del despacho'
                WHEN 'v1' THEN N'Se rechaza el veh' + NCHAR(237) + N'culo'
                WHEN 'v2' THEN N'Validaci' + NCHAR(243) + N'n por Sofasa'
                ELSE N'Se deroga (se acepta el defecto)' END;
            DELETE FROM dbo.tblPanelesDano WHERE Codigo NOT IN ('superior', 'perfil_posterior_izquierdo', 'perfil_posterior_derecho', 'costado_izquierdo', 'costado_derecho');
            UPDATE dbo.tblPanelesDano SET Activo = 1, ImagenPath = NULL;
            UPDATE dbo.tblZonasDano SET Activo = 1 WHERE CuadranteId IS NULL;
            UPDATE dbo.tblVistasFoto SET Activo = 1, TipoVehiculo = NULL, Obligatoria = CASE WHEN Codigo IN ('piezas_baul', 'cojineria') THEN 0 ELSE 1 END;
            """;
        await using var conexion = new SqlConnection(CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(SQL, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>Ejecuta una sentencia suelta sobre la base de la prueba (para forzar estados, por ejemplo un token vencido).</summary>
    public async Task EjecutarAsync(string sql)
    {
        await using var conexion = new SqlConnection(CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task EjecutarScriptAsync(string cadena, string archivo)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "database", archivo);
        var contenido = await File.ReadAllTextAsync(ruta);
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();

        // sqlcmd usa -I (QUOTED_IDENTIFIER ON); aqui se fija explicitamente para vistas e indices filtrados.
        await using (var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion))
        {
            await preparar.ExecuteNonQueryAsync();
        }

        foreach (var lote in SeparadorGo().Split(contenido).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            await using var comando = new SqlCommand(lote, conexion) { CommandTimeout = 120 };
            await comando.ExecuteNonQueryAsync();
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();
}

[CollectionDefinition(NOMBRE)]
public sealed class clsColeccionBaseDatos : ICollectionFixture<clsBaseDatosFixture>
{
    public const string NOMBRE = "BaseDatos";
}
