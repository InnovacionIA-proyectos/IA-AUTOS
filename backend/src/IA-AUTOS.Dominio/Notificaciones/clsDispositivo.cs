using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Notificaciones;

public static class clsPlataformaDispositivo
{
    public const string ANDROID = "android";
    public const string IOS = "ios";
    public const string WEB = "web";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { ANDROID, IOS, WEB };
}

/// <summary>El celular (o navegador) donde un usuario recibe las notificaciones push. El token de FCM es unico: si el celular cambia de usuario, el token pasa al nuevo.</summary>
public sealed class clsDispositivo : clsEntidadBase
{
    public const int TOKEN_MAX = 512;
    public const int MODELO_MAX = 100;

    private clsDispositivo()
    {
    }

    public Guid UsuarioId { get; private set; }

    public string TokenFcm { get; private set; } = string.Empty;

    public string Plataforma { get; private set; } = string.Empty;

    public string? Modelo { get; private set; }

    public DateTimeOffset UltimoUsoAt { get; private set; }

    public bool Activo { get; private set; } = true;

    public static clsDispositivo Registrar(Guid usuarioId, string? tokenFcm, string? plataforma, string? modelo, DateTimeOffset ahora)
    {
        var dispositivo = new clsDispositivo
        {
            UsuarioId = usuarioId,
            TokenFcm = TokenValido(tokenFcm),
            Plataforma = PlataformaValida(plataforma),
            Modelo = ModeloValido(modelo),
            UltimoUsoAt = ahora,
        };
        dispositivo.Inicializar(ahora);
        return dispositivo;
    }

    /// <summary>El mismo token vuelve a registrarse (quizas con otro usuario, otro modelo o despues de cerrar sesion): queda activo y a nombre de quien lo registra.</summary>
    public void Actualizar(Guid usuarioId, string? plataforma, string? modelo, DateTimeOffset ahora)
    {
        UsuarioId = usuarioId;
        Plataforma = PlataformaValida(plataforma);
        Modelo = ModeloValido(modelo);
        Activo = true;
        UltimoUsoAt = ahora;
        Tocar(ahora);
    }

    /// <summary>Ya no recibe push: cerro la sesion o FCM informo que el token no es valido.</summary>
    public void Desactivar(DateTimeOffset ahora)
    {
        if (!Activo)
        {
            return;
        }

        Activo = false;
        Tocar(ahora);
    }

    public static string TokenValido(string? token)
    {
        var limpio = clsValidador.Opcional(token) ?? throw clsErroresCampos.De("token_fcm", "El token de FCM es requerido");
        return limpio.Length <= TOKEN_MAX ? limpio : throw clsErroresCampos.De("token_fcm", $"El token admite máximo {TOKEN_MAX} caracteres");
    }

    private static string PlataformaValida(string? plataforma) =>
        plataforma is not null && clsPlataformaDispositivo.VALORES.Contains(plataforma)
            ? plataforma
            : throw clsErroresCampos.De("plataforma", $"Plataforma inválida: use {string.Join(", ", clsPlataformaDispositivo.VALORES.Order())}");

    private static string? ModeloValido(string? modelo)
    {
        var limpio = clsValidador.Opcional(modelo);
        return limpio is { Length: > MODELO_MAX }
            ? throw clsErroresCampos.De("modelo", $"El modelo admite máximo {MODELO_MAX} caracteres")
            : limpio;
    }
}
