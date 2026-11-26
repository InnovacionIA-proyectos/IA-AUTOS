using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;

namespace IAAutos.Dominio.Catalogos;

/// <summary>Para quien es un tipo de novedad del viaje: solo para el conductor, solo para el inspector o para los dos.</summary>
public static class clsRolDeNovedadViaje
{
    public const string CONDUCTOR = "conductor";
    public const string INSPECTOR = "inspector";
    public const string TODOS = "todos";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { CONDUCTOR, INSPECTOR, TODOS };

    /// <summary>True si quien reporta con ese rol puede elegir un tipo con ese <paramref name="rolDelTipo"/>.</summary>
    public static bool Admite(string rolDelTipo, string rolDeQuienReporta) => rolDelTipo == TODOS || rolDelTipo == rolDeQuienReporta;
}

/// <summary>
/// Un tipo de novedad del viaje (algo que le pasa al viaje y no a un carro: una falla del camion, un bloqueo de via, un carro que no llego al patio...). Trae el rol de quien lo puede reportar
/// (<see cref="Rol"/>) y la urgencia del aviso que genera (<see cref="Urgencia"/>, <c>alta</c> o <c>media</c>). Un tipo que una novedad usa no se borra (409): se inactiva.
/// </summary>
public sealed class clsTipoNovedadViaje : clsCatalogoCodificado
{
    private clsTipoNovedadViaje()
    {
    }

    public string Rol { get; private set; } = clsRolDeNovedadViaje.TODOS;

    public string Urgencia { get; private set; } = clsUrgenciaNotificacion.MEDIA;

    public static clsTipoNovedadViaje Crear(string? codigo, string? nombre, string? rol, string? urgencia, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var tipo = new clsTipoNovedadViaje { Rol = RolValido(rol ?? clsRolDeNovedadViaje.TODOS), Urgencia = UrgenciaValida(urgencia ?? clsUrgenciaNotificacion.MEDIA) };
        tipo.Iniciar(codigo, nombre, orden, activo, ahora);
        return tipo;
    }

    /// <summary>Cambia el rol y la urgencia del tipo; un valor nulo conserva el actual.</summary>
    public void Cambiar(string? rol, string? urgencia, DateTimeOffset ahora)
    {
        Rol = rol is null ? Rol : RolValido(rol);
        Urgencia = urgencia is null ? Urgencia : UrgenciaValida(urgencia);
        Tocar(ahora);
    }

    private static string RolValido(string rol) =>
        clsRolDeNovedadViaje.VALORES.Contains(rol.Trim()) ? rol.Trim() : throw clsErroresCampos.De("rol", "El rol debe ser conductor, inspector o todos");

    private static string UrgenciaValida(string urgencia) =>
        clsUrgenciaNotificacion.VALORES.Contains(urgencia.Trim()) ? urgencia.Trim() : throw clsErroresCampos.De("urgencia", "La urgencia debe ser alta o media");
}
