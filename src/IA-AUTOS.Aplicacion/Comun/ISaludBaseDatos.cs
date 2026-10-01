namespace IAAutos.Aplicacion.Comun;

public sealed record EstadoBaseDatos(bool Conectada, long? LatenciaMs);

/// <summary>Comprueba la conexion con la base de datos (endpoint /health).</summary>
public interface ISaludBaseDatos
{
    Task<EstadoBaseDatos> VerificarAsync(CancellationToken ct = default);
}
