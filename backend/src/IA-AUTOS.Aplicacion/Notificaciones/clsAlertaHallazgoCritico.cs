using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Notificaciones;

/// <summary>
/// La alerta de hallazgo critico: cuando una inspeccion se cierra con un hallazgo de nivel critico, o cuando se revisa una novedad del conductor y recibe un nivel critico, avisa con urgencia alta, por
/// bandeja y push, a cada coordinador de calidad y administrador activo del cliente del vehiculo. Los niveles criticos son el parametro <c>niveles_criticos</c> (por defecto V1+ y V1). Hay una alerta por
/// inspeccion: un segundo hallazgo critico de la misma inspeccion no crea otra.
/// </summary>
public sealed class clsAlertaHallazgoCritico(IUsuarioRepository usuarios, clsEmisorNotificaciones emisor, IParametros parametros)
{
    private static readonly string[] POR_DEFECTO = [clsCodigosNivel.V1_PLUS, clsCodigosNivel.V1];

    public async Task<bool> EsCriticoAsync(string? nivel, CancellationToken ct = default) =>
        nivel is not null && (await parametros.ListaAsync(clsClavesParametros.NIVELES_CRITICOS, POR_DEFECTO, ct)).Contains(nivel, StringComparer.Ordinal);

    /// <summary>
    /// Emite la alerta de la inspeccion si alguno de los hallazgos tiene un nivel critico; si no, no hace nada. Nunca lanza: un fallo al avisar no deshace lo que se avisa.
    /// </summary>
    public async Task EmitirSiHayCriticosAsync(IReadOnlyCollection<clsHallazgo> hallazgos, clsVehiculo vehiculo, Guid viajeId, string proceso, CancellationToken ct = default)
    {
        var niveles = await parametros.ListaAsync(clsClavesParametros.NIVELES_CRITICOS, POR_DEFECTO, ct);
        var criticos = hallazgos.Where(h => h.NivelSeveridad is { } nivel && niveles.Contains(nivel, StringComparer.Ordinal)).ToList();
        if (criticos.Count == 0)
        {
            return;
        }

        var primero = criticos[0];
        var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], vehiculo.ClienteId, ct);
        var nivelesTexto = string.Join(", ", criticos.Select(h => Texto(h.NivelSeveridad)).Distinct());
        await emisor.EmitirAsync(
            new clsAlerta(
                clsTipoNotificacion.HALLAZGO_CRITICO,
                clsUrgenciaNotificacion.ALTA,
                destinatarios.Select(d => d.Id).ToList(),
                vehiculo.ClienteId,
                viajeId,
                vehiculo.Id,
                primero.Id,
                primero.InspeccionId,
                $"Hallazgo crítico: {vehiculo.Vin}",
                $"La inspección de {proceso} de la unidad {vehiculo.Vin} tiene {criticos.Count} hallazgo(s) de nivel crítico ({nivelesTexto}).",
                $"{clsTipoNotificacion.HALLAZGO_CRITICO}:{primero.InspeccionId}"),
            ct);
    }

    private static string Texto(string? nivel) => nivel?.Replace("_plus", "+", StringComparison.Ordinal).ToUpperInvariant() ?? string.Empty;
}
