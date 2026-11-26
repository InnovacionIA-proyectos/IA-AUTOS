using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Viajes;

/// <summary>El proceso del viaje al que se refiere la novedad que reporta un inspector.</summary>
public static class clsProcesoDeNovedadViaje
{
    public const string CARGUE = "cargue";
    public const string ENTREGA = "entrega";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { CARGUE, ENTREGA };
}

/// <summary>La foto sellada de una novedad del viaje, tal como la guarda la novedad: el archivo subido, el sello del celular y el hash SHA-256 que calculo el servidor.</summary>
public sealed record clsFotoNovedadViaje(string StoragePath, DateTimeOffset SelloFecha, double SelloLat, double SelloLng, double? Precision, string? DispositivoId, string HashSha256)
{
    public const int DISPOSITIVO_MAX = 100;

    /// <summary>Una foto tomada sin GPS queda en (0, 0): «sin ubicacion», como en las demas evidencias.</summary>
    public bool SinUbicacion => SelloLat == 0 && SelloLng == 0;

    public static clsFotoNovedadViaje Crear(string storagePath, DateTimeOffset selloFecha, double? selloLat, double? selloLng, double? precision, string? dispositivoId, string hashSha256)
    {
        clsValidador.Latitud(selloLat, "sello_lat");
        clsValidador.Longitud(selloLng, "sello_lng");
        if (precision is { } metros && (metros < 0 || !double.IsFinite(metros)))
        {
            throw clsErroresCampos.De("precision", "La precisión debe ser un número en metros mayor o igual a 0");
        }

        var dispositivo = clsValidador.Opcional(dispositivoId);
        if (dispositivo is { Length: > DISPOSITIVO_MAX })
        {
            throw clsErroresCampos.De("dispositivo_id", $"El dispositivo admite máximo {DISPOSITIVO_MAX} caracteres");
        }

        var conUbicacion = selloLat is not null && selloLng is not null;
        return new clsFotoNovedadViaje(storagePath.Trim(), selloFecha, conUbicacion ? selloLat!.Value : 0, conUbicacion ? selloLng!.Value : 0, precision, dispositivo, hashSha256);
    }
}

/// <summary>
/// Una novedad del viaje: algo que le pasa al viaje y no a un carro (una falla del camion, un bloqueo de via, un carro que no llego al patio). La reporta el conductor del viaje o un inspector que participa, con su
/// tipo del catalogo <c>tipos-novedad-viaje</c>, un texto de hasta 300 caracteres, la fecha y el GPS del celular (el GPS es opcional: sin el la novedad queda «sin ubicacion») y, si quiere, una foto sellada. No es un
/// hallazgo ni cuelga de un carro: no toca ninguna unidad, ninguna inspeccion, ningun conteo ni el estado del viaje. No se edita ni se borra (la base de datos lo impide). <see cref="IdEnvio"/> es el identificador
/// que el celular le pone a cada envio para que un reenvio no la duplique.
/// </summary>
public sealed class clsNovedadViaje
{
    public const int TEXTO_MAX = 300;

    private clsNovedadViaje()
    {
    }

    public Guid Id { get; private set; }

    public Guid ViajeId { get; private set; }

    /// <summary>El codigo del tipo (catalogo <c>tipos-novedad-viaje</c>).</summary>
    public string TipoCodigo { get; private set; } = string.Empty;

    public Guid UsuarioId { get; private set; }

    /// <summary>El rol de quien la reporto: <c>conductor</c> o <c>inspector</c>.</summary>
    public string Rol { get; private set; } = string.Empty;

    /// <summary>Solo en la que reporta un inspector: <c>cargue</c> o <c>entrega</c>.</summary>
    public string? Proceso { get; private set; }

    /// <summary>La parada, en la novedad que un inspector reporta sobre la entrega.</summary>
    public Guid? LugarId { get; private set; }

    public string Texto { get; private set; } = string.Empty;

    /// <summary>Cuando ocurrio, segun el celular (se conserva aunque llegue despues, sin senal).</summary>
    public DateTimeOffset Fecha { get; private set; }

    public double? Lat { get; private set; }

    public double? Lng { get; private set; }

    /// <summary>El identificador del envio que puso el celular; nulo en los envios de la app anterior.</summary>
    public Guid? IdEnvio { get; private set; }

    public string? FotoStoragePath { get; private set; }

    public DateTimeOffset? FotoSelloFecha { get; private set; }

    public double? FotoSelloLat { get; private set; }

    public double? FotoSelloLng { get; private set; }

    public double? FotoPrecision { get; private set; }

    public string? FotoDispositivoId { get; private set; }

    public string? FotoHashSha256 { get; private set; }

    /// <summary>Cuando llego al servidor.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public bool ConFoto => FotoStoragePath is not null;

    /// <summary>Sin latitud y longitud completas: la novedad queda «sin ubicacion».</summary>
    public bool SinUbicacion => Lat is null || Lng is null;

    public static clsNovedadViaje Crear(
        Guid viajeId,
        string tipoCodigo,
        Guid usuarioId,
        string rol,
        string? proceso,
        Guid? lugarId,
        string? texto,
        DateTimeOffset? fecha,
        double? lat,
        double? lng,
        Guid? idEnvio,
        clsFotoNovedadViaje? foto,
        DateTimeOffset ahora)
    {
        var limpio = clsValidador.Opcional(texto) ?? throw clsErroresCampos.De("texto", "Escribe qué pasó");
        if (limpio.Length > TEXTO_MAX)
        {
            throw clsErroresCampos.De("texto", $"Escribe máximo {TEXTO_MAX} caracteres");
        }

        var momento = fecha ?? throw clsErroresCampos.De("fecha", "La fecha es requerida");
        clsValidador.Latitud(lat);
        clsValidador.Longitud(lng);
        if ((lat is null) != (lng is null))
        {
            throw clsErroresCampos.De(lat is null ? "lat" : "lng", "Envíe la latitud y la longitud juntas, o ninguna si no hay GPS");
        }

        if (proceso is not null && !clsProcesoDeNovedadViaje.VALORES.Contains(proceso))
        {
            throw clsErroresCampos.De("proceso", "El proceso debe ser cargue o entrega");
        }

        return new clsNovedadViaje
        {
            Id = Guid.NewGuid(),
            ViajeId = viajeId,
            TipoCodigo = tipoCodigo,
            UsuarioId = usuarioId,
            Rol = rol,
            Proceso = proceso,
            LugarId = lugarId,
            Texto = limpio,
            Fecha = momento,
            Lat = lat,
            Lng = lng,
            IdEnvio = idEnvio,
            FotoStoragePath = foto?.StoragePath,
            FotoSelloFecha = foto?.SelloFecha,
            FotoSelloLat = foto?.SelloLat,
            FotoSelloLng = foto?.SelloLng,
            FotoPrecision = foto?.Precision,
            FotoDispositivoId = foto?.DispositivoId,
            FotoHashSha256 = foto?.HashSha256,
            CreatedAt = ahora,
        };
    }
}

public interface INovedadViajeRepository
{
    void Agregar(clsNovedadViaje novedad);

    /// <summary>La novedad del viaje que ya se guardo con ese identificador de envio, o nula.</summary>
    Task<clsNovedadViaje?> ObtenerPorEnvioAsync(Guid viajeId, Guid idEnvio, CancellationToken ct = default);
}
