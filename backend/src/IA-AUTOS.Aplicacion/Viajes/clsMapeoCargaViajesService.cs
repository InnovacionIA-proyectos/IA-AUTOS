using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>Una columna del archivo: su titulo y su numero de repeticion (1 la primera vez que el titulo aparece, 2 la segunda...). El titulo se normaliza en el servidor.</summary>
public sealed record ColumnaDelArchivoRequest(string? Encabezado, int? Ocurrencia);

/// <summary>La forma de un archivo: la lista de sus columnas (titulo y repeticion), en el orden que traiga.</summary>
public sealed record BuscarMapeoCargaRequest(IReadOnlyList<ColumnaDelArchivoRequest>? Columnas);

/// <summary>
/// El mapeo que se guarda: las <c>Columnas</c> del archivo y, en el mismo orden, el <c>Campos</c> elegido para cada una (<c>vin</c>, <c>placa</c>, <c>trailer</c>, <c>cedula_conductor</c>, <c>unidad</c>, <c>codigo_mtrack</c>,
/// <c>fecha_programada</c>, <c>origen</c> o <c>destino</c>; nulo = se ignora). <c>Version</c> es 1.
/// </summary>
public sealed record GuardarMapeoCargaRequest(int? Version, IReadOnlyList<ColumnaDelArchivoRequest>? Columnas, IReadOnlyList<string?>? Campos);

public sealed record ColumnaDelArchivoDto(string Encabezado, int Ocurrencia);

/// <summary>El mapeo guardado, con quien lo guardo por ultima vez y cuando. Solo titulos y campos: ningun valor de las filas.</summary>
public sealed record MapeoCargaViajesDto(int Version, IReadOnlyList<ColumnaDelArchivoDto> Columnas, IReadOnlyList<string?> Campos, Guid ActualizadoPor, string? ActualizadoPorNombre, DateTimeOffset ActualizadoAt);

/// <summary>
/// El mapeo de columnas de la carga de viajes se guarda por cliente y por «forma del archivo» (los titulos normalizados —sin tildes, en minuscula y con espacios simples— con su numero de repeticion, sin importar el orden
/// de las columnas): dos archivos del mismo cliente con titulos distintos conviven y ninguno pisa al otro. Lo leen y lo guardan quienes cargan viajes (coordinador de calidad, admin y super admin); un cliente nunca recibe el
/// de otro (la lectura responde «sin mapeo» como si no existiera) y el super administrador indica el cliente. Cada guardado queda en la auditoria con quien y cuando, solo con titulos y campos.
/// </summary>
public sealed partial class clsMapeoCargaViajesService(
    IMapeoCargaViajesRepository mapeos,
    IUsuarioRepository usuarios,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj)
{
    public const int VERSION = 1;
    public const int COLUMNAS_MAX = 200;
    public const int ENCABEZADO_MAX = 200;

    /// <summary>Los campos de una fila de la carga que se pueden elegir para una columna.</summary>
    public static readonly IReadOnlySet<string> CAMPOS = new HashSet<string> { "vin", "placa", "trailer", "cedula_conductor", "unidad", "codigo_mtrack", "fecha_programada", "origen", "destino" };

    private static readonly JsonSerializerOptions JSON = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>El mapeo guardado para esa forma de archivo, o 404 «Sin mapeo» si el cliente no tiene uno (o si fuera de otro cliente: nunca se distingue).</summary>
    public async Task<MapeoCargaViajesDto> BuscarAsync(string? clienteId, BuscarMapeoCargaRequest solicitud, CancellationToken ct = default)
    {
        var cliente = ClienteDeLaConsulta(clienteId);
        var errores = new Dictionary<string, string[]>();
        var columnas = Columnas(solicitud.Columnas, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var mapeo = await mapeos.ObtenerAsync(cliente, Huella(columnas), ct) ?? throw new clsEntidadNoEncontradaException("Sin mapeo");
        return await ADtoAsync(mapeo, ct);
    }

    /// <summary>
    /// Guarda o actualiza el mapeo de esa forma de archivo (el orden de las columnas no cambia la forma). 400 si faltan columnas o campos, si no van en la misma cantidad, si un campo no existe o va en dos columnas, o si una
    /// repeticion no va de 1 a N sin saltos. Queda en la auditoria (<c>viajes.mapeo.guardar</c>) con quien y cuando, solo con titulos y campos.
    /// </summary>
    public async Task<MapeoCargaViajesDto> GuardarAsync(string? clienteId, GuardarMapeoCargaRequest solicitud, CancellationToken ct = default)
    {
        var cliente = ClienteDeLaConsulta(clienteId);
        var errores = new Dictionary<string, string[]>();
        if (solicitud.Version is { } version && version != VERSION)
        {
            errores["version"] = [$"La versión del mapeo es {VERSION}"];
        }

        var columnas = Columnas(solicitud.Columnas, errores);
        var campos = Campos(solicitud.Campos, columnas.Count, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var huella = Huella(columnas);
        var jsonColumnas = JsonSerializer.Serialize(columnas.Select(c => new ColumnaDelArchivoDto(c.Encabezado, c.Ocurrencia)), JSON);
        var jsonCampos = JsonSerializer.Serialize(campos, JSON);
        var ahora = reloj.Ahora;
        var existente = await mapeos.ObtenerAsync(cliente, huella, ct);
        if (existente is null)
        {
            existente = clsMapeoCargaViajes.Crear(cliente, huella, jsonColumnas, jsonCampos, actual.IdObligatorio(), ahora);
            mapeos.Agregar(existente);
        }
        else
        {
            existente.Actualizar(jsonColumnas, jsonCampos, actual.IdObligatorio(), ahora);
        }

        auditor.Registrar(
            "viajes.mapeo.guardar",
            "mapeos_carga_viajes",
            existente.Id,
            detalle: new DetalleAuditoria(null, new { cliente_id = cliente, huella, columnas = columnas.Select((c, i) => new { encabezado = c.Encabezado, ocurrencia = c.Ocurrencia, campo = campos[i] }).ToList() }),
            clienteId: cliente);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ADtoAsync(existente, ct);
    }

    /// <summary>
    /// El cliente del mapeo: quien se limita a su cliente recibe el suyo (403 si pide otro) y el super administrador debe indicarlo con <c>cliente_id</c> (400 si no lo hace o si no es un UUID).
    /// </summary>
    private Guid ClienteDeLaConsulta(string? clienteId)
    {
        var errores = new Dictionary<string, string[]>();
        var solicitado = clsParseoFiltros.Uuid(clienteId, "cliente_id", errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        return actual.ClienteDelFiltro(solicitado) ?? throw clsErroresCampos.De("cliente_id", "Indique el cliente del mapeo");
    }

    private async Task<MapeoCargaViajesDto> ADtoAsync(clsMapeoCargaViajes mapeo, CancellationToken ct)
    {
        var columnas = JsonSerializer.Deserialize<List<ColumnaDelArchivoDto>>(mapeo.Columnas, JSON) ?? [];
        var campos = JsonSerializer.Deserialize<List<string?>>(mapeo.Campos, JSON) ?? [];
        var nombre = (await usuarios.ObtenerPorIdAsync(mapeo.ActualizadoPor, ct))?.Nombre;
        return new MapeoCargaViajesDto(VERSION, columnas, campos, mapeo.ActualizadoPor, nombre, mapeo.UpdatedAt);
    }

    // ------------------------------------------------------------------------------------------------ la forma del archivo

    private sealed record Columna(string Encabezado, int Ocurrencia);

    /// <summary>Las columnas con su titulo normalizado: al menos una, sin pasar del maximo, sin titulos vacios y con cada repeticion de 1 a N sin saltos ni repetirse.</summary>
    private static List<Columna> Columnas(IReadOnlyList<ColumnaDelArchivoRequest>? recibidas, Dictionary<string, string[]> errores)
    {
        var resultado = new List<Columna>();
        if (recibidas is null or { Count: 0 })
        {
            errores["columnas"] = ["Indique los títulos de las columnas del archivo"];
            return resultado;
        }

        if (recibidas.Count > COLUMNAS_MAX)
        {
            errores["columnas"] = [$"Un archivo admite máximo {COLUMNAS_MAX} columnas"];
            return resultado;
        }

        var problemas = new List<string>();
        foreach (var (columna, i) in recibidas.Select((c, i) => (c, i)))
        {
            var titulo = Normalizar(columna.Encabezado);
            if (titulo.Length == 0 || titulo.Length > ENCABEZADO_MAX)
            {
                problemas.Add($"La columna {i + 1} debe traer un título de hasta {ENCABEZADO_MAX} caracteres");
                continue;
            }

            if (columna.Ocurrencia is not { } ocurrencia || ocurrencia < 1)
            {
                problemas.Add($"La columna «{titulo}» debe traer su número de repetición (1 la primera vez que el título aparece)");
                continue;
            }

            resultado.Add(new Columna(titulo, ocurrencia));
        }

        foreach (var grupo in resultado.GroupBy(c => c.Encabezado))
        {
            var repeticiones = grupo.Select(c => c.Ocurrencia).Order().ToList();
            if (!repeticiones.SequenceEqual(Enumerable.Range(1, repeticiones.Count)))
            {
                problemas.Add($"Las repeticiones del título «{grupo.Key}» deben ir de 1 a {repeticiones.Count} sin saltos ni repetirse");
            }
        }

        if (problemas.Count > 0)
        {
            errores["columnas"] = [.. problemas];
        }

        return resultado;
    }

    /// <summary>Un campo por columna, en el mismo orden: uno de <see cref="CAMPOS"/> o nulo (se ignora la columna); un mismo campo no va en dos columnas.</summary>
    private static List<string?> Campos(IReadOnlyList<string?>? recibidos, int columnas, Dictionary<string, string[]> errores)
    {
        var resultado = new List<string?>();
        if (recibidos is null)
        {
            errores["campos"] = ["Indique el campo elegido para cada columna (nulo si se ignora)"];
            return resultado;
        }

        if (columnas > 0 && recibidos.Count != columnas)
        {
            errores["campos"] = [$"Debe traer un campo por columna: {columnas} columnas y {recibidos.Count} campos"];
            return resultado;
        }

        var problemas = new List<string>();
        foreach (var recibido in recibidos)
        {
            var campo = string.IsNullOrWhiteSpace(recibido) ? null : recibido.Trim().ToLowerInvariant();
            if (campo is not null && !CAMPOS.Contains(campo))
            {
                problemas.Add($"El campo «{recibido}» no existe: use {string.Join(", ", CAMPOS.Order())}");
            }

            resultado.Add(campo);
        }

        foreach (var repetido in resultado.Where(c => c is not null).GroupBy(c => c).Where(g => g.Count() > 1))
        {
            problemas.Add($"El campo «{repetido.Key}» está elegido para más de una columna");
        }

        if (problemas.Count > 0)
        {
            errores["campos"] = [.. problemas];
        }

        return resultado;
    }

    /// <summary>El titulo normalizado: sin tildes, en minuscula y con los espacios simples y recortados.</summary>
    public static string Normalizar(string? titulo)
    {
        if (string.IsNullOrWhiteSpace(titulo))
        {
            return string.Empty;
        }

        var sinTildes = new StringBuilder();
        foreach (var c in titulo.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sinTildes.Append(c);
            }
        }

        return Espacios().Replace(sinTildes.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), " ").Trim();
    }

    /// <summary>La huella de la forma: el SHA-256 (hexadecimal) de los titulos normalizados con su repeticion, ordenados, para que el orden de las columnas no importe.</summary>
    private static string Huella(IEnumerable<Columna> columnas) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', columnas.Select(c => $"{c.Encabezado}#{c.Ocurrencia}").Order(StringComparer.Ordinal))))).ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();
}
