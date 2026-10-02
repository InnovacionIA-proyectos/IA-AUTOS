using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>Arma escenarios directamente por SQL (independiente de los endpoints bajo prueba).</summary>
public sealed class clsSembrador(string cadenaConexion)
{
    public async Task<Guid[]> PuntosRutaAsync()
    {
        var ids = new List<Guid>();
        foreach (var nombre in new[] { "cargue", "transporte", "descargue" })
        {
            ids.Add((Guid)(await EscalarAsync("SELECT Id FROM dbo.tblPuntosCustodia WHERE Nombre = @n", ("@n", nombre)))!);
        }

        return [.. ids];
    }

    public async Task<(Guid VehiculoId, Guid[] EtapaIds)> VehiculoConRutaAsync(
        string vin,
        string marca = "Renault",
        string operacion = "nacional",
        int cantidadEtapas = 3)
    {
        var vehiculoId = Guid.NewGuid();
        await EjecutarAsync(
            "INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion) VALUES (@id, @vin, @marca, @op)",
            ("@id", vehiculoId), ("@vin", vin), ("@marca", marca), ("@op", operacion));
        var puntos = await PuntosRutaAsync();
        var etapas = new List<Guid>();
        for (var i = 0; i < cantidadEtapas; i++)
        {
            var etapaId = Guid.NewGuid();
            await EjecutarAsync(
                "INSERT dbo.tblEtapas (Id, VehiculoId, PuntoCustodiaId, Orden) VALUES (@id, @v, @p, @o)",
                ("@id", etapaId), ("@v", vehiculoId), ("@p", puntos[i]), ("@o", i + 1));
            etapas.Add(etapaId);
        }

        return (vehiculoId, [.. etapas]);
    }

    /// <summary>Crea un informe con hallazgos y evidencias; si queda cerrado, lo cierra al final (los triggers impiden agregar despues).</summary>
    public async Task<Guid> InformeAsync(Guid etapaId, Guid usuarioId, bool cerrada, int hallazgos = 0, int evidencias = 0, string momento = "informe")
    {
        var id = Guid.NewGuid();
        await EjecutarAsync(
            "INSERT dbo.tblInspecciones (Id, EtapaId, UsuarioId, Momento, Fecha, Lat, Lng, Estado) VALUES (@id, @e, @u, @m, SYSDATETIMEOFFSET(), 4.6, -74.1, 'borrador')",
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
