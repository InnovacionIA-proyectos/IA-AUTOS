using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Viajes;

/// <summary>
/// El mapeo de columnas de la carga de viajes de un cliente para una forma de archivo: la <see cref="Huella"/> identifica la forma (los titulos normalizados con su numero de repeticion, sin importar el orden de las columnas),
/// <see cref="Columnas"/> son esos titulos tal como se guardaron y <see cref="Campos"/> el campo elegido para cada uno (nulo = se ignora la columna), los dos como JSON. Solo guarda titulos y campos: nunca valores de las filas.
/// Lo comparten quienes cargan viajes del mismo cliente; lo guardado por ultima vez dice quien y cuando.
/// </summary>
public sealed class clsMapeoCargaViajes : clsEntidadBase
{
    private clsMapeoCargaViajes()
    {
    }

    public Guid ClienteId { get; private set; }

    /// <summary>El hash SHA-256 en hexadecimal (64 caracteres) de la forma del archivo.</summary>
    public string Huella { get; private set; } = string.Empty;

    public string Columnas { get; private set; } = "[]";

    public string Campos { get; private set; } = "[]";

    public Guid ActualizadoPor { get; private set; }

    public static clsMapeoCargaViajes Crear(Guid clienteId, string huella, string columnas, string campos, Guid usuarioId, DateTimeOffset ahora)
    {
        var mapeo = new clsMapeoCargaViajes { ClienteId = clienteId, Huella = huella, Columnas = columnas, Campos = campos, ActualizadoPor = usuarioId };
        mapeo.Inicializar(ahora);
        return mapeo;
    }

    /// <summary>Reemplaza lo guardado por lo ultimo que se confirmo, con quien lo hizo y cuando.</summary>
    public void Actualizar(string columnas, string campos, Guid usuarioId, DateTimeOffset ahora)
    {
        Columnas = columnas;
        Campos = campos;
        ActualizadoPor = usuarioId;
        Tocar(ahora);
    }
}

public interface IMapeoCargaViajesRepository
{
    /// <summary>El mapeo de ese cliente para esa forma de archivo, o nulo si no hay.</summary>
    Task<clsMapeoCargaViajes?> ObtenerAsync(Guid clienteId, string huella, CancellationToken ct = default);

    void Agregar(clsMapeoCargaViajes mapeo);
}
