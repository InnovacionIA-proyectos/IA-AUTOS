using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Usuarios;

/// <summary>
/// Token de refresco de una sesion del portal. Solo se guarda el hash (SHA-256) del valor que recibe el navegador en su
/// cookie. Cada uso lo rota: el token usado queda revocado y apunta al que lo reemplazo. Todos los tokens de una misma
/// sesion comparten la familia: si alguien presenta un token ya rotado (posible robo), se revoca la familia completa.
/// </summary>
public sealed class clsRefreshToken : clsEntidadBase
{
    private clsRefreshToken()
    {
    }

    public Guid UsuarioId { get; private set; }

    /// <summary>SHA-256 del token en hexadecimal (64 caracteres).</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Identifica la sesion; tambien viaja en el access token (claim sid) para poder revocarlo.</summary>
    public Guid FamiliaId { get; private set; }

    public DateTimeOffset ExpiraAt { get; private set; }

    public DateTimeOffset? RevocadoAt { get; private set; }

    /// <summary>Token que lo reemplazo al rotar. Nulo si se revoco por cierre de sesion o por reuso.</summary>
    public Guid? ReemplazadoPorId { get; private set; }

    public string? Ip { get; private set; }

    public string? Dispositivo { get; private set; }

    public static clsRefreshToken Emitir(
        Guid usuarioId,
        string tokenHash,
        Guid familiaId,
        TimeSpan vigencia,
        string? ip,
        string? dispositivo,
        DateTimeOffset ahora)
    {
        var token = new clsRefreshToken
        {
            UsuarioId = usuarioId,
            TokenHash = clsValidador.Requerido(tokenHash, "Token de refresco inválido"),
            FamiliaId = familiaId,
            ExpiraAt = ahora + vigencia,
            Ip = Recortar(ip, 45),
            Dispositivo = Recortar(dispositivo, 300),
        };
        token.Inicializar(ahora);
        return token;
    }

    public bool EstaVigente(DateTimeOffset ahora) => RevocadoAt is null && ExpiraAt > ahora;

    /// <summary>Marca el token como usado al rotarlo; <paramref name="reemplazadoPorId"/> es el token nuevo.</summary>
    public void RevocarPorRotacion(Guid reemplazadoPorId, DateTimeOffset ahora)
    {
        RevocadoAt = ahora;
        ReemplazadoPorId = reemplazadoPorId;
        Tocar(ahora);
    }

    /// <summary>Revoca el token (cierre de sesion o reuso). No hace nada si ya estaba revocado.</summary>
    public void Revocar(DateTimeOffset ahora)
    {
        if (RevocadoAt is not null)
        {
            return;
        }

        RevocadoAt = ahora;
        Tocar(ahora);
    }

    private static string? Recortar(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var limpio = valor.Trim();
        return limpio.Length <= maximo ? limpio : limpio[..maximo];
    }
}
