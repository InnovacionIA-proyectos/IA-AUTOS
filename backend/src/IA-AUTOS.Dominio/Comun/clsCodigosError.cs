namespace IAAutos.Dominio.Comun;

/// <summary>Codigos de error estables de la API. El cliente puede ramificar por codigo.</summary>
public static class clsCodigosError
{
    public const string VALIDATION_ERROR = "VALIDATION_ERROR";
    public const string UNAUTHORIZED = "UNAUTHORIZED";
    public const string FORBIDDEN = "FORBIDDEN";
    public const string NOT_FOUND = "NOT_FOUND";
    public const string METHOD_NOT_ALLOWED = "METHOD_NOT_ALLOWED";
    public const string CONFLICT = "CONFLICT";
    public const string NOT_IMPLEMENTED = "NOT_IMPLEMENTED";
    public const string DB_UNAVAILABLE = "DB_UNAVAILABLE";
    public const string INTERNAL_ERROR = "INTERNAL_ERROR";
    public const string RATE_LIMITED = "RATE_LIMITED";

    /// <summary>La cuenta esta bloqueada por intentos de PIN fallidos (423); details.bloqueado_hasta dice hasta cuando.</summary>
    public const string ACCOUNT_LOCKED = "ACCOUNT_LOCKED";

    /// <summary>El PIN fue asignado o reiniciado y hay que cambiarlo antes de usar el resto de la API (403).</summary>
    public const string PIN_CHANGE_REQUIRED = "PIN_CHANGE_REQUIRED";

    /// <summary>La persona del portal no tiene acceso habilitado a IA Autos (no existe en el portal, no tiene un rol de IA Autos o fue retirada): 403 con codigo propio, distinto de FORBIDDEN (rol insuficiente), para que el modulo muestre el mensaje de acceso no habilitado.</summary>
    public const string PORTAL_USER_NOT_LINKED = "PORTAL_USER_NOT_LINKED";
}
