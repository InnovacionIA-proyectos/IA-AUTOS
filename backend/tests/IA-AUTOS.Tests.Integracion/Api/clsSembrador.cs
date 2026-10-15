using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>Arma escenarios directamente por SQL (independiente de los endpoints bajo prueba).</summary>
public sealed class clsSembrador(string cadenaConexion)
{
    /// <summary>Ids de las etapas del catalogo que forman la ruta estandar (cargue, transporte, descargue).</summary>
    public async Task<Guid[]> EtapasRutaAsync()
    {
        var ids = new List<Guid>();
        foreach (var nombre in new[] { "cargue", "transporte", "descargue" })
        {
            ids.Add((Guid)(await EscalarAsync("SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = @n", ("@n", nombre)))!);
        }

        return [.. ids];
    }

    /// <summary>
    /// Un vehiculo con un viaje (nuevo, o el indicado) y las etapas de su ruta. El viaje nuevo toma el conductor y el inspector que se
    /// indiquen: sin ellos solo lo ven los administradores y el coordinador.
    /// </summary>
    public async Task<(Guid VehiculoId, Guid[] EtapaIds)> VehiculoConRutaAsync(
        string vin,
        string marca = "Renault",
        string operacion = "nacional",
        int cantidadEtapas = 3,
        Guid? viajeId = null,
        Guid? conductorId = null,
        Guid? inspectorId = null)
    {
        var vehiculoId = Guid.NewGuid();
        await EjecutarAsync(
            "INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion) VALUES (@id, @vin, @marca, @op)",
            ("@id", vehiculoId), ("@vin", vin), ("@marca", marca), ("@op", operacion));
        var viaje = viajeId ?? await ViajeAsync(conductorId, inspectorId);
        await EjecutarAsync(
            "INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @viaje, @v)",
            ("@viaje", viaje), ("@v", vehiculoId));
        var etapasCatalogo = await EtapasRutaAsync();
        var etapas = new List<Guid>();
        for (var i = 0; i < cantidadEtapas; i++)
        {
            var etapaId = Guid.NewGuid();
            await EjecutarAsync(
                "INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@id, @v, @viaje, @p, @o)",
                ("@id", etapaId), ("@v", vehiculoId), ("@viaje", viaje), ("@p", etapasCatalogo[i]), ("@o", i + 1));
            etapas.Add(etapaId);
        }

        return (vehiculoId, [.. etapas]);
    }

    /// <summary>Un vehiculo suelto: sin viaje ni etapas (recien cargado).</summary>
    public async Task<Guid> VehiculoAsync(string vin, string marca = "Renault", string operacion = "nacional")
    {
        var id = Guid.NewGuid();
        await EjecutarAsync(
            "INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion) VALUES (@id, @vin, @marca, @op)",
            ("@id", id), ("@vin", vin), ("@marca", marca), ("@op", operacion));
        return id;
    }

    private int _viajes;

    /// <summary>Un viaje sin VIN (programado, con placa y codigo MTrack unicos) con el conductor y el inspector indicados.</summary>
    public async Task<Guid> ViajeAsync(Guid? conductorId = null, Guid? inspectorId = null, string estado = "programado", string? codigoMtrack = null)
    {
        var id = Guid.NewGuid();
        var n = Interlocked.Increment(ref _viajes);
        var tipo = (Guid)(await EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        await EjecutarAsync(
            "INSERT dbo.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, ConductorId, InspectorId, Estado) VALUES (@id, @codigo, @tipo, @placa, @c, @i, @estado)",
            ("@id", id), ("@codigo", codigoMtrack ?? $"MTK-S{n:0000}-{id.ToString()[..6]}"), ("@tipo", tipo), ("@placa", $"PLA{n:000}"),
            ("@c", (object?)conductorId ?? DBNull.Value), ("@i", (object?)inspectorId ?? DBNull.Value), ("@estado", estado));
        return id;
    }

    public async Task<Guid> ViajeDeEtapaAsync(Guid etapaId) =>
        (Guid)(await EscalarAsync("SELECT ViajeId FROM dbo.tblEtapas WHERE Id = @e", ("@e", etapaId)))!;

    /// <summary>Crea un informe con hallazgos y evidencias; si queda cerrado, lo cierra al final (los triggers impiden agregar despues).</summary>
    public async Task<Guid> InformeAsync(Guid etapaId, Guid usuarioId, bool cerrada, int hallazgos = 0, int evidencias = 0, string momento = "informe")
    {
        var id = Guid.NewGuid();
        await EjecutarAsync(
            "INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Lat, Lng, Estado) SELECT @id, e.Id, e.ViajeId, @u, @m, SYSDATETIMEOFFSET(), 4.6, -74.1, 'borrador' FROM dbo.tblEtapas e WHERE e.Id = @e",
            ("@id", id), ("@e", etapaId), ("@u", usuarioId), ("@m", momento));
        for (var i = 0; i < hallazgos; i++)
        {
            await EjecutarAsync(
                "INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano, CoordsDiagrama) VALUES (NEWID(), @i, N'Capot', 'golpe', '1_5cm', N'{\"x\":0.5,\"y\":0.25}')",
                ("@i", id));
        }

        for (var i = 0; i < evidencias; i++)
        {
            await EjecutarAsync(
                "INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), @i, @p, SYSDATETIMEOFFSET(), 4.6, -74.1)",
                ("@i", id), ("@p", $"foto-{i}.jpg"));
        }

        if (cerrada)
        {
            await EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada' WHERE Id = @i", ("@i", id));
        }

        return id;
    }

    /// <summary>Informe con hallazgos concretos (parte, clase de dano, tamano); si queda cerrado, se cierra al final.</summary>
    public async Task<Guid> InformeConHallazgosAsync(Guid etapaId, Guid usuarioId, bool cerrada, params (string Parte, string Clase, string Tamano)[] hallazgos)
    {
        var id = await InformeAsync(etapaId, usuarioId, cerrada: false);
        foreach (var (parte, clase, tamano) in hallazgos)
        {
            await EjecutarAsync(
                "INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), @i, @p, @c, @t)",
                ("@i", id), ("@p", parte), ("@c", clase), ("@t", tamano));
        }

        if (cerrada)
        {
            await EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada' WHERE Id = @i", ("@i", id));
        }

        return id;
    }

    public async Task EjecutarAsync(string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var conexion = new SqlConnection(cadenaConexion);
        await conexion.OpenAsync();
        await using var comando = Crear(conexion, sql, parametros);
        await comando.ExecuteNonQueryAsync();
    }

    public async Task<object?> EscalarAsync(string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var conexion = new SqlConnection(cadenaConexion);
        await conexion.OpenAsync();
        await using var comando = Crear(conexion, sql, parametros);
        var valor = await comando.ExecuteScalarAsync();
        return valor is DBNull ? null : valor;
    }

    public async Task<int> ContarAsync(string sql, params (string Nombre, object Valor)[] parametros) =>
        Convert.ToInt32(await EscalarAsync(sql, parametros));

    private static SqlCommand Crear(SqlConnection conexion, string sql, (string Nombre, object Valor)[] parametros)
    {
        var comando = new SqlCommand(sql, conexion);
        foreach (var (nombre, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nombre, valor);
        }

        return comando;
    }
}
