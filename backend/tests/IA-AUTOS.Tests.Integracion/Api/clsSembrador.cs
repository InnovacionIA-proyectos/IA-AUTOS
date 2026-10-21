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
            "INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) SELECT @id, @vin, @marca, @op, Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'",
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
            "INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) SELECT @id, @vin, @marca, @op, Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'",
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
    public async Task<Guid> InformeAsync(Guid etapaId, Guid usuarioId, bool cerrada, int hallazgos = 0, int evidencias = 0, string momento = "salida")
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
                "INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, CreatedAt) VALUES (NEWID(), @i, @p, SYSDATETIMEOFFSET(), 4.6, -74.1, DATEADD(MILLISECOND, @n, SYSDATETIMEOFFSET()))",
                ("@i", id), ("@p", $"foto-{i}.jpg"), ("@n", i * 5));
        }

        if (cerrada)
        {
            await EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i", ("@i", id));
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
            await EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i", ("@i", id));
        }

        return id;
    }

    /// <summary>Crea un cuadrante; la base le crea sus zonas (una por lado del 1 al 25, una sola del 26 en adelante).</summary>
    public async Task<Guid> CuadranteAsync(int numero, string nombre, string? tipoVehiculo = null)
    {
        var id = Guid.NewGuid();
        await EjecutarAsync(
            "INSERT dbo.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre, Orden) VALUES (@id, @tipo, @numero, @nombre, @numero)",
            ("@id", id), ("@tipo", (object?)tipoVehiculo ?? DBNull.Value), ("@numero", numero), ("@nombre", nombre));
        return id;
    }

    /// <summary>Un hallazgo con zona (provisional «frente») y nivel, de la inspeccion indicada: la forma que guarda la API.</summary>
    public async Task<Guid> HallazgoEnFrenteAsync(Guid inspeccionId, string nivel = "v1_plus", string clase = "golpe")
    {
        var id = Guid.NewGuid();
        await EjecutarAsync(
            "INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) SELECT @id, @i, z.Id, @c, '5_10cm', @n FROM dbo.tblZonasDano z WHERE z.Codigo = 'frente'",
            ("@id", id), ("@i", inspeccionId), ("@c", clase), ("@n", nivel));
        return id;
    }

    /// <summary>
    /// Una inspeccion ya cerrada con un hallazgo en la zona indicada (la forma que guarda la API): de nivel V2 por defecto, con o sin su foto propia y de origen inspector
    /// (o conductor, sin nivel, como una novedad que nadie reviso).
    /// </summary>
    public async Task<(Guid InspeccionId, Guid HallazgoId)> InformeCerradoConHallazgoAsync(
        Guid etapaId,
        Guid usuarioId,
        Guid zonaId,
        string clase = "golpe",
        string momento = "salida",
        string? nivel = "v2",
        bool conFoto = false,
        string origen = "inspector",
        bool cerrada = true)
    {
        var inspeccion = await InformeAsync(etapaId, usuarioId, cerrada: false, momento: momento);
        var hallazgo = Guid.NewGuid();
        await EjecutarAsync(
            "INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad, Origen) VALUES (@h, @i, @z, @c, '5_10cm', @n, @o)",
            ("@h", hallazgo), ("@i", inspeccion), ("@z", zonaId), ("@c", clase), ("@n", (object?)nivel ?? DBNull.Value), ("@o", origen));
        if (conFoto)
        {
            await EjecutarAsync(
                "INSERT dbo.tblEvidencias (Id, InspeccionId, HallazgoId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), @i, @h, @p, SYSDATETIMEOFFSET(), 4.6, -74.1)",
                ("@i", inspeccion), ("@h", hallazgo), ("@p", $"hallazgo-{hallazgo}.png"));
        }

        if (cerrada)
        {
            await EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i", ("@i", inspeccion));
        }

        return (inspeccion, hallazgo);
    }

    /// <summary>
    /// La transferencia de la inspeccion ya cerrada de una forma: <c>firmada</c>, <c>con_reservas</c> o <c>no_firma</c> (con su testigo y la foto de la constancia); sin tipo
    /// queda abierta, con solo la firma de quien entrega.
    /// </summary>
    public async Task TransferenciaAsync(Guid inspeccionId, Guid entregaId, Guid recibeId, string? tipoCierre, Guid? testigoId = null)
    {
        Guid? constancia = null;
        if (tipoCierre == "no_firma")
        {
            constancia = Guid.NewGuid();
            await EjecutarAsync(
                "INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, EsConstancia) VALUES (@e, @i, 'constancia.png', SYSDATETIMEOFFSET(), 4.6, -74.1, 1)",
                ("@e", constancia.Value), ("@i", inspeccionId));
        }

        await EjecutarAsync(
            "INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmadaAt, TipoCierre, ReservasTexto, NoFirmaParte, MotivoNoFirma, TestigoUsuarioId, EvidenciaConstanciaId) "
            + "VALUES (NEWID(), @i, @e, @r, @fe, @fr, @fa, @t, @res, @np, @mn, @te, @ec)",
            ("@i", inspeccionId), ("@e", entregaId), ("@r", recibeId),
            ("@fe", (tipoCierre is "firmada" or "con_reservas" or null) ? "A" : (object)DBNull.Value),
            ("@fr", (tipoCierre is "firmada" or "con_reservas") ? "B" : (object)DBNull.Value),
            ("@fa", (tipoCierre is null) ? DBNull.Value : (object)DateTimeOffset.UtcNow),
            ("@t", (object?)tipoCierre ?? DBNull.Value),
            ("@res", tipoCierre == "con_reservas" ? "Rayon en la puerta" : (object)DBNull.Value),
            ("@np", tipoCierre == "no_firma" ? "recibe" : (object)DBNull.Value),
            ("@mn", tipoCierre == "no_firma" ? "Se nego a firmar" : (object)DBNull.Value),
            ("@te", (object?)testigoId ?? DBNull.Value),
            ("@ec", (object?)constancia ?? DBNull.Value));
    }

    /// <summary>La zona de un cuadrante por numero y lado (nulo del 26 en adelante).</summary>
    public async Task<Guid> ZonaAsync(int numero, string? lado) =>
        (Guid)(await EscalarAsync(
            "SELECT Id FROM dbo.tblZonasDano WHERE Numero = @n AND ((@l IS NULL AND Lado IS NULL) OR Lado = @l)",
            ("@n", numero), ("@l", (object?)lado ?? DBNull.Value)))!;

    /// <summary>Marca la unidad como «no sale» del viaje, con el hallazgo que la causo y quien la marco.</summary>
    public Task UnidadNoSaleAsync(Guid viajeId, Guid vehiculoId, Guid hallazgoId, Guid marcadoPor, string motivo, Guid? reemplazoId = null) =>
        EjecutarAsync(
            "UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'no_sale', NoSaleMotivo = @m, NoSaleHallazgoId = @h, NoSaleMarcadoPor = @u, NoSaleAt = SYSDATETIMEOFFSET(), ReemplazoVehiculoId = @r WHERE ViajeId = @v AND VehiculoId = @veh",
            ("@m", motivo), ("@h", hallazgoId), ("@u", marcadoPor), ("@r", (object?)reemplazoId ?? DBNull.Value), ("@v", viajeId), ("@veh", vehiculoId));

    /// <summary>Las vistas de foto activas (obligatorias o adicionales) del catalogo, en su orden: id y nombre.</summary>
    public async Task<List<(Guid Id, string Nombre)>> VistasAsync(bool obligatoria)
    {
        await using var conexion = new SqlConnection(cadenaConexion);
        await conexion.OpenAsync();
        await using var comando = Crear(conexion, "SELECT Id, Nombre FROM dbo.tblVistasFoto WHERE Obligatoria = @o AND Activo = 1 ORDER BY Orden", [("@o", obligatoria)]);
        await using var lector = await comando.ExecuteReaderAsync();
        var vistas = new List<(Guid, string)>();
        while (await lector.ReadAsync())
        {
            vistas.Add((lector.GetGuid(0), lector.GetString(1)));
        }

        return vistas;
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
