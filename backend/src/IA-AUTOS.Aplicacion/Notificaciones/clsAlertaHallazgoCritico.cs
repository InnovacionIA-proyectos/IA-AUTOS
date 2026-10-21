using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Notificaciones;

/// <summary>
/// Los niveles de severidad que se consideran criticos y alertan al coordinador y al administrador del cliente apenas se asignan. Se leen de la configuracion (seccion <c>Alertas</c>,
/// <c>Alertas__NivelesCriticos__0</c>...); por defecto solo V1+.
/// </summary>
public sealed class clsOpcionesAlertas
{
    public const string SECCION = "Alertas";

    public string[] NivelesCriticos { get; set; } = [clsCodigosNivel.V1_PLUS];
}

/// <summary>
/// La alerta de hallazgo critico: cuando un hallazgo tiene un nivel critico, avisa con urgencia alta, por bandeja y push, a cada coordinador de calidad y administrador activo del cliente del
/// vehiculo. Una alerta por hallazgo: repetirla no crea otra.
/// </summary>
public sealed class clsAlertaHallazgoCritico(IUsuarioRepository usuarios, clsEmisorNotificaciones emisor, IOptions<clsOpcionesAlertas> opciones)
{
    public bool EsCritico(string? nivel) => nivel is not null && opciones.Value.NivelesCriticos.Contains(nivel, StringComparer.Ordinal);

    /// <summary>Emite la alerta si el nivel del hallazgo es critico; si no, no hace nada. Nunca lanza: un fallo al avisar no deshace lo que se avisa.</summary>
    public async Task EmitirSiEsCriticoAsync(clsHallazgo hallazgo, clsVehiculo vehiculo, Guid viajeId, CancellationToken ct = default)
    {
        if (!EsCritico(hallazgo.NivelSeveridad))
        {
            return;
        }

        var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], vehiculo.ClienteId, ct);
        await emisor.EmitirAsync(
            new clsAlerta(
                clsTipoNotificacion.HALLAZGO_CRITICO,
                clsUrgenciaNotificacion.ALTA,
                destinatarios.Select(d => d.Id).ToList(),
                vehiculo.ClienteId,
                viajeId,
                vehiculo.Id,
                hallazgo.Id,
                hallazgo.InspeccionId,
                $"Hallazgo crítico: {vehiculo.Vin}",
                $"La unidad {vehiculo.Vin} tiene un hallazgo de nivel {Texto(hallazgo.NivelSeveridad)}{(hallazgo.ClaseDano is { } clase ? $" ({clase.Replace('_', ' ')})" : string.Empty)}.",
                $"{clsTipoNotificacion.HALLAZGO_CRITICO}:{hallazgo.Id}"),
            ct);
    }

    private static string Texto(string? nivel) => nivel?.Replace("_plus", "+", StringComparison.Ordinal).ToUpperInvariant() ?? string.Empty;
}
