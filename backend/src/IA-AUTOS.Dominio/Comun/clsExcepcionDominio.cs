namespace IAAutos.Dominio.Comun;

/// <summary>Excepcion base de reglas de negocio. Lleva un codigo estable de error.</summary>
public abstract class clsExcepcionDominio : Exception
{
    protected clsExcepcionDominio(string codigo, string mensaje, object? detalles = null)
        : base(mensaje)
    {
        Codigo = codigo;
        Detalles = detalles;
    }

    public string Codigo { get; }

    public object? Detalles { get; }
}

public sealed class clsValidacionException : clsExcepcionDominio
{
    public clsValidacionException(string mensaje, object? detalles = null)
        : base(clsCodigosError.VALIDATION_ERROR, mensaje, detalles)
    {
    }
}

public sealed class clsEntidadNoEncontradaException : clsExcepcionDominio
{
    public clsEntidadNoEncontradaException(string mensaje)
        : base(clsCodigosError.NOT_FOUND, mensaje)
    {
    }
}

public sealed class clsConflictoException : clsExcepcionDominio
{
    public clsConflictoException(string mensaje)
        : base(clsCodigosError.CONFLICT, mensaje)
    {
    }
}

public sealed class clsNoAutenticadoException : clsExcepcionDominio
{
    public clsNoAutenticadoException(string mensaje)
        : base(clsCodigosError.UNAUTHORIZED, mensaje)
    {
    }
}

public sealed class clsProhibidoException : clsExcepcionDominio
{
    public clsProhibidoException(string mensaje)
        : base(clsCodigosError.FORBIDDEN, mensaje)
    {
    }
}

/// <summary>Condicion interna inesperada (por ejemplo, catalogo incompleto). Se responde 500 sin detalles.</summary>
public sealed class clsErrorInternoException : clsExcepcionDominio
{
    public clsErrorInternoException(string mensaje)
        : base(clsCodigosError.INTERNAL_ERROR, mensaje)
    {
    }
}

/// <summary>La persona del portal no tiene acceso habilitado a IA Autos (sin rol, no vinculada o retirada): 403 PORTAL_USER_NOT_LINKED.</summary>
public sealed class clsPortalUsuarioNoVinculadoException : clsExcepcionDominio
{
    public clsPortalUsuarioNoVinculadoException(string mensaje)
        : base(clsCodigosError.PORTAL_USER_NOT_LINKED, mensaje)
    {
    }
}

/// <summary>La cuenta esta bloqueada por demasiados PIN errados: 423 ACCOUNT_LOCKED con la hora en que se levanta.</summary>
public sealed class clsCuentaBloqueadaException : clsExcepcionDominio
{
    public clsCuentaBloqueadaException(DateTimeOffset bloqueadoHasta)
        : base(
            clsCodigosError.ACCOUNT_LOCKED,
            "Cuenta bloqueada por intentos fallidos. Intenta de nuevo más tarde o pide a tu administrador que la desbloquee.",
            new Dictionary<string, object> { ["bloqueado_hasta"] = bloqueadoHasta.UtcDateTime })
    {
    }
}

/// <summary>El PIN es temporal: hasta cambiarlo (POST /api/auth/cambiar-pin) el resto de la API responde 403 PIN_CHANGE_REQUIRED.</summary>
public sealed class clsCambioPinRequeridoException : clsExcepcionDominio
{
    public clsCambioPinRequeridoException()
        : base(clsCodigosError.PIN_CHANGE_REQUIRED, "Debes cambiar tu PIN antes de continuar")
    {
    }
}

/// <summary>Un servicio externo necesario (por ejemplo, la base de usuarios del portal) no respondio: 503, nunca se concede acceso.</summary>
public sealed class clsServicioNoDisponibleException : clsExcepcionDominio
{
    public clsServicioNoDisponibleException(string mensaje)
        : base(clsCodigosError.DB_UNAVAILABLE, mensaje)
    {
    }
}
