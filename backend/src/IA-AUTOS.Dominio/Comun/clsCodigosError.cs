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

    /// <summary>La etapa anterior del VIN no tiene su inspeccion de llegada cerrada: no se puede crear la salida de la siguiente (409).</summary>
    public const string ETAPA_PREVIA_SIN_CIERRE = "ETAPA_PREVIA_SIN_CIERRE";

    /// <summary>La llegada de Transporte de esa unidad ya estaba cerrada (409): un reintento del conductor es un exito, el servidor no la repite.</summary>
    public const string LLEGADA_YA_CERRADA = "LLEGADA_YA_CERRADA";

    /// <summary>La URL de la foto no tiene firma, la firma no coincide o ya vencio (403): hay que pedir una URL nueva al sistema.</summary>
    public const string URL_FIRMADA_INVALIDA = "URL_FIRMADA_INVALIDA";

    /// <summary>La novedad del conductor ya fue revisada por un inspector (409): su zona, clase y nivel ya no se cambian.</summary>
    public const string HALLAZGO_YA_REVISADO = "HALLAZGO_YA_REVISADO";

    /// <summary>La unidad se marco «no sale» del viaje y ya no se transporta (409): no admite novedades ni llegada.</summary>
    public const string UNIDAD_NO_SALE = "UNIDAD_NO_SALE";

    /// <summary>El PIN fue asignado o reiniciado y hay que cambiarlo antes de usar el resto de la API (403).</summary>
    public const string PIN_CHANGE_REQUIRED = "PIN_CHANGE_REQUIRED";

    /// <summary>La persona del portal no tiene acceso habilitado a IA Autos (no existe en el portal, no tiene un rol de IA Autos o fue retirada): 403 con codigo propio, distinto de FORBIDDEN (rol insuficiente), para que el modulo muestre el mensaje de acceso no habilitado.</summary>
    public const string PORTAL_USER_NOT_LINKED = "PORTAL_USER_NOT_LINKED";
}
