using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Comun;

/// <summary>
/// Visibilidad por cliente en lectura. Cada usuario de un cliente (coordinador de calidad, comercial, administrador...) ve solo las unidades de su cliente
/// (<c>tblVehiculos.ClienteId</c>) y lo que cuelga de ellas; el super administrador ve todos los clientes y es el unico que puede pedir uno distinto del
/// suyo. El inspector y el conductor no se limitan por cliente sino por viaje (ver <see cref="clsUsuarioActualExtensiones.SoloSusViajes"/>).
/// </summary>
public static class clsAlcanceCliente
{
    /// <summary>
    /// El cliente al que se limita la lectura de quien consulta: nulo si no tiene restriccion por cliente (super administrador, inspector y conductor) y, si es de
    /// un cliente pero no tiene ninguno asignado, <see cref="Guid.Empty"/> (no coincide con ninguna unidad: no ve nada).
    /// </summary>
    public static Guid? ClienteRestringido(this IUsuarioActual actual) =>
        actual.Rol is null or clsRol.SUPER_ADMIN || clsRol.VeSoloSusViajes(actual.Rol) ? null : actual.ClienteId ?? Guid.Empty;

    /// <summary>
    /// El cliente por el que se filtra una consulta que admite elegirlo (<c>cliente_id</c>): el super administrador y quien no se limita por cliente eligen cualquiera
    /// (o ninguno); quien se limita a su cliente siempre recibe el suyo, y pedir uno distinto responde 403.
    /// </summary>
    public static Guid? ClienteDelFiltro(this IUsuarioActual actual, Guid? solicitado)
    {
        if (actual.ClienteRestringido() is not { } propio)
        {
            return solicitado;
        }

        if (solicitado is { } otro && otro != propio)
        {
            throw new clsProhibidoException("Solo puede consultar lo de su propio cliente");
        }

        return propio;
    }
}
