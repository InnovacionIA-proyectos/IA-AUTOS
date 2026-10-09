namespace IAAutos.Dominio.Usuarios;

public interface IRefreshTokenRepository
{
    Task<clsRefreshToken?> ObtenerPorHashAsync(string tokenHash, CancellationToken ct = default);

    void Agregar(clsRefreshToken token);

    /// <summary>
    /// True si la sesion (familia) tiene algun token vigente. Si todos estan revocados o vencidos, la sesion se cerro y
    /// su access token deja de servir aunque no haya expirado.
    /// </summary>
    Task<bool> FamiliaActivaAsync(Guid familiaId, DateTimeOffset ahora, CancellationToken ct = default);

    /// <summary>
    /// Revoca todos los tokens vigentes de la sesion. Se confirma de inmediato (fuera de la unidad de trabajo): debe quedar
    /// guardado aunque la solicitud que lo origina termine con un error 401.
    /// </summary>
    Task RevocarFamiliaAsync(Guid familiaId, DateTimeOffset ahora, CancellationToken ct = default);
}
