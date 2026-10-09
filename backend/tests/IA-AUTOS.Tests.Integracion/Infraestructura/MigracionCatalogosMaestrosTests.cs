using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/02-catalogos-maestros.sql, PR #128 / issue #82 - QA) contra un
/// SQL Server 2019 con el esquema y los datos PREVIOS a esta PR (el que ya esta desplegado), para confirmar que la migracion no
/// pierde ni corrompe datos existentes. Los demas tests de integracion usan ddl.sql ya migrado (base nueva, sin datos previos) y
/// nunca ejercitan el script de migracion en si mismo contra datos reales.
/// </summary>
public sealed class MigracionCatalogosMaestrosTests : IAsyncLifetime
{
    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadenaConexion = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracion");
        var constructor = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracion" };
        _cadenaConexion = constructor.ConnectionString;
        await EjecutarAsync(_cadenaConexion, ESQUEMA_PREVIO);
    }

    public async Task DisposeAsync() => await _contenedor.DisposeAsync();

    private static async Task EjecutarAsync(string cadena, string sql)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 60 };
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<T?> EscalarAsync<T>(string cadena, string sql)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 30 };
        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? default : (T)Convert.ChangeType(resultado, typeof(T));
    }

    /// <summary>El esquema de dbo.tblVehiculos, dbo.tblPuntosCustodia y dbo.tblEtapas tal como estaban ANTES de esta PR (ddl.sql en QA
    /// antes de mergear S5-HU-21), con una ruta de 3 etapas real (cargue, transporte, descargue) ya creada.</summary>
    private const string ESQUEMA_PREVIO = """
        CREATE TABLE dbo.tblVehiculos
        (
            Id            uniqueidentifier NOT NULL CONSTRAINT PK_tblVehiculos PRIMARY KEY,
            Vin           varchar(64)      NOT NULL,
            Marca         nvarchar(100)    NOT NULL,
            Operacion     varchar(20)      NOT NULL,
            CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVehiculos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
            UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVehiculos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
            CONSTRAINT UQ_tblVehiculos_Vin UNIQUE (Vin),
            CONSTRAINT CK_tblVehiculos_Operacion CHECK (Operacion IN ('importacion', 'exportacion', 'nacional'))
        );

        CREATE TABLE dbo.tblPuntosCustodia
        (
            Id         uniqueidentifier NOT NULL CONSTRAINT PK_tblPuntosCustodia PRIMARY KEY,
            Nombre     varchar(30)      NOT NULL,
            CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPuntosCustodia_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
            UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPuntosCustodia_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
            CONSTRAINT UQ_tblPuntosCustodia_Nombre UNIQUE (Nombre),
            CONSTRAINT CK_tblPuntosCustodia_Nombre CHECK (Nombre IN
                ('cargue', 'punto_origen', 'certificadora', 'punto_destino', 'transporte', 'descargue', 'concesionario'))
        );
        INSERT INTO dbo.tblPuntosCustodia (Id, Nombre)
        SELECT NEWID(), v.Nombre
        FROM (VALUES ('cargue'), ('punto_origen'), ('certificadora'), ('punto_destino'),
                     ('transporte'), ('descargue'), ('concesionario')) AS v (Nombre);

        CREATE TABLE dbo.tblEtapas
        (
            Id                  uniqueidentifier NOT NULL CONSTRAINT PK_tblEtapas PRIMARY KEY,
            VehiculoId          uniqueidentifier NOT NULL,
            PuntoCustodiaId     uniqueidentifier NOT NULL,
            Orden               int              NOT NULL,
            RequiereInspeccion  bit              NOT NULL CONSTRAINT DF_tblEtapas_RequiereInspeccion DEFAULT (1),
            CreatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapas_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
            UpdatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapas_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
            CONSTRAINT FK_tblEtapas_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id) ON DELETE CASCADE,
            CONSTRAINT FK_tblEtapas_PuntoCustodia FOREIGN KEY (PuntoCustodiaId) REFERENCES dbo.tblPuntosCustodia (Id),
            CONSTRAINT UQ_tblEtapas_Vehiculo_Orden UNIQUE (VehiculoId, Orden),
            CONSTRAINT CK_tblEtapas_Orden CHECK (Orden >= 1)
        );
        CREATE INDEX IX_tblEtapas_PuntoCustodia ON dbo.tblEtapas (PuntoCustodiaId);

        CREATE TABLE dbo.tblInspecciones
        (
            Id      uniqueidentifier NOT NULL CONSTRAINT PK_tblInspecciones PRIMARY KEY,
            EtapaId uniqueidentifier NOT NULL,
            CONSTRAINT FK_tblInspecciones_Etapa FOREIGN KEY (EtapaId) REFERENCES dbo.tblEtapas (Id) ON DELETE CASCADE
        );

        CREATE TABLE dbo.tblHallazgos
        (
            Id              uniqueidentifier NOT NULL CONSTRAINT PK_tblHallazgos PRIMARY KEY,
            InspeccionId    uniqueidentifier NOT NULL,
            ParteVehiculo   nvarchar(200)    NOT NULL,
            ClaseDano       varchar(30)      NOT NULL,
            TamanoDano      varchar(30)      NOT NULL,
            CONSTRAINT FK_tblHallazgos_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES dbo.tblInspecciones (Id) ON DELETE CASCADE,
            CONSTRAINT CK_tblHallazgos_ClaseDano CHECK (ClaseDano IN
                ('deformacion', 'rotura', 'cortado', 'perforacion', 'golpe',
                 'mancha', 'raya_pintar', 'desconchado', 'particulas', 'faltante')),
            CONSTRAINT CK_tblHallazgos_TamanoDano CHECK (TamanoDano IN
                ('1_5cm', '5_10cm', '10_20cm', 'mas_20cm', 'requiere_reemplazo', 'mas_10_particulas'))
        );

        DECLARE @vehiculo uniqueidentifier = NEWID();
        INSERT INTO dbo.tblVehiculos (Id, Vin, Marca, Operacion) VALUES (@vehiculo, 'VIN-MIGRACION-0001', 'Renault', 'importacion');

        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Nombre = 'cargue');
        DECLARE @transporte uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Nombre = 'transporte');
        DECLARE @descargue uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Nombre = 'descargue');
        INSERT INTO dbo.tblEtapas (Id, VehiculoId, PuntoCustodiaId, Orden) VALUES
            ('11111111-1111-1111-1111-111111111111', @vehiculo, @cargue, 1),
            ('22222222-2222-2222-2222-222222222222', @vehiculo, @transporte, 2),
            ('33333333-3333-3333-3333-333333333333', @vehiculo, @descargue, 3);

        DECLARE @inspeccion uniqueidentifier = NEWID();
        INSERT INTO dbo.tblInspecciones (Id, EtapaId) VALUES (@inspeccion, '11111111-1111-1111-1111-111111111111');
        INSERT INTO dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano)
        VALUES ('44444444-4444-4444-4444-444444444444', @inspeccion, 'puerta_delantera_izquierda', 'raya_pintar', '5_10cm');
        """;

    [Fact]
    public async Task La_migracion_preserva_las_etapas_y_los_hallazgos_existentes_sin_perdida()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "02-catalogos-maestros.sql");
        var script = (await File.ReadAllTextAsync(ruta)).Replace("N'db_IA_AUTOS'", "N'dbo'");

        await EjecutarAsync(_cadenaConexion, script);

        // Las 3 etapas que ya existian antes de migrar siguen ahi (mismo Id), con su orden, y ahora apuntan a la etapa del
        // catalogo que corresponde a su antiguo punto de custodia (cargue/transporte/descargue), sin lugar asignado todavia.
        await using var conexion = new SqlConnection(_cadenaConexion);
        await conexion.OpenAsync();

        async Task<(Guid EtapaCatalogoId, string Codigo, bool LugarNulo)> LeerEtapa(string id)
        {
            await using var comando = new SqlCommand("""
                SELECT e.EtapaCatalogoId, c.Codigo, IIF(e.PuntoCustodiaId IS NULL, 1, 0)
                  FROM dbo.tblEtapas e JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId
                 WHERE e.Id = @id
                """, conexion);
            comando.Parameters.AddWithValue("@id", Guid.Parse(id));
            await using var lector = await comando.ExecuteReaderAsync();
            Assert.True(await lector.ReadAsync(), $"La etapa {id} desaparecio en la migracion.");
            return (lector.GetGuid(0), lector.GetString(1), lector.GetInt32(2) == 1);
        }

        var cargue = await LeerEtapa("11111111-1111-1111-1111-111111111111");
        var transporte = await LeerEtapa("22222222-2222-2222-2222-222222222222");
        var descargue = await LeerEtapa("33333333-3333-3333-3333-333333333333");

        Assert.Equal("cargue", cargue.Codigo);
        Assert.Equal("transporte", transporte.Codigo);
        Assert.Equal("descargue", descargue.Codigo);
        Assert.True(cargue.LugarNulo && transporte.LugarNulo && descargue.LugarNulo, "El lugar debe quedar sin asignar tras migrar, no inventado.");

        // El hallazgo (con su clase y tamano reales) sigue existiendo y ahora cumple la FK nueva sin perder su valor.
        var hallazgoClase = await EscalarAsync<string>(_cadenaConexion,
            "SELECT ClaseDano FROM dbo.tblHallazgos WHERE Id = '44444444-4444-4444-4444-444444444444'");
        Assert.Equal("raya_pintar", hallazgoClase);
        var fkClaseDano = await EscalarAsync<int>(_cadenaConexion,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblHallazgos_ClaseDano'");
        Assert.Equal(1, fkClaseDano);

        // Nadie quedo sin migrar y los conteos semilla son los esperados.
        Assert.Equal(0, await EscalarAsync<int>(_cadenaConexion, "SELECT COUNT(*) FROM dbo.tblEtapas WHERE EtapaCatalogoId IS NULL"));
        Assert.Equal(3, await EscalarAsync<int>(_cadenaConexion, "SELECT COUNT(*) FROM dbo.tblEtapasCatalogo"));
        Assert.Equal(6, await EscalarAsync<int>(_cadenaConexion, "SELECT COUNT(*) FROM dbo.tblPuntosCustodia"));
        Assert.Equal(10, await EscalarAsync<int>(_cadenaConexion, "SELECT COUNT(*) FROM dbo.tblClasesDano"));
    }
}
