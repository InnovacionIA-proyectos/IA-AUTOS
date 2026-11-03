using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Lo que lleva el aviso cuando la primera foto de una inspeccion quedo «fuera del rango».</summary>
public sealed record AvisoFueraDeRango(Guid InspeccionId, Guid ViajeId, Guid VehiculoId, string Proceso, string? Lugar, double DistanciaM, double RadioM, double? PrecisionM);

/// <summary>
/// Confirma «en sitio» la evidencia sellada de una inspeccion de Cargue o Descargue: con la primera foto con ubicacion calcula la distancia al lugar de la etapa (el lugar de cargue del viaje o el destino de
/// la unidad) y aplica <see cref="clsConfirmacionSitio"/>. El resultado se guarda en la inspeccion (mientras esta en borrador) y, solo si queda «fuera del rango», se avisa una vez al coordinador de calidad del
/// cliente por bandeja y push. NUNCA bloquea ni cambia la captura: la foto se guarda y se sella igual y, si algo falla en el calculo, el resultado es «sin confirmacion posible». El radio sale del lugar (si lo
/// trae) o del parametro del cargue o del destino, y la valvula de precision del parametro: se leen en cada evaluacion.
/// </summary>
public sealed class clsConfirmacionEnSitioService(
    IEtapaRepository etapas,
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IUsuarioRepository usuarios,
    ICatalogosLectura catalogos,
    IParametros parametros,
    clsEmisorNotificaciones emisor,
    ILogger<clsConfirmacionEnSitioService> registro)
{
    public const int RADIO_CARGUE_POR_DEFECTO_M = 1000;
    public const int RADIO_DESTINO_POR_DEFECTO_M = 500;
    public const int VALVULA_POR_DEFECTO_M = 500;

    /// <summary>
    /// Evalua la foto recien agregada y deja el resultado en la inspeccion (sin guardar: lo confirma quien llama junto con la foto). Una vez determinado (confirmado en sitio o fuera del rango) no cambia; «sin
    /// confirmacion posible» se reevalua con la siguiente foto que traiga ubicacion. Devuelve el aviso a enviar si la inspeccion acaba de quedar fuera del rango.
    /// </summary>
    public async Task<AvisoFueraDeRango?> EvaluarAsync(clsInspeccion inspeccion, clsEvidencia foto, DateTimeOffset ahora, CancellationToken ct = default)
    {
        var actual = inspeccion.ConfirmacionSitio;
        if (inspeccion.EstaCerrada || foto.EsConstancia || foto.EsSello
            || actual is clsResultadoConfirmacionSitio.CONFIRMADO_EN_SITIO or clsResultadoConfirmacionSitio.FUERA_DE_RANGO
            || (actual is clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE && foto.SinUbicacion))
        {
            return null;
        }

        clsEtapa? etapa = null;
        string? proceso = null;
        string? nombreDelLugar = null;
        var radio = 0d;
        clsEvaluacionSitio evaluacion;
        try
        {
            etapa = await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct);
            proceso = etapa is null ? null : (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo;
            if (etapa is null || proceso is not (clsCodigosEtapa.CARGUE or clsCodigosEtapa.DESCARGUE))
            {
                // Solo el Cargue y el Descargue se confirman: el Transporte es el recorrido y no tiene lugar.
                return null;
            }

            var lugarId = proceso == clsCodigosEtapa.CARGUE
                ? (await viajes.ObtenerPorIdAsync(etapa.ViajeId, ct))?.LugarCargueId
                : (await viajes.ObtenerVehiculoAsync(etapa.ViajeId, etapa.VehiculoId, ct))?.LugarDestinoId;
            var lugar = lugarId is { } id ? (await catalogos.ValoresAsync(clsNombresCatalogo.LUGARES, ct)).FirstOrDefault(l => l.Id == id) : null;
            nombreDelLugar = lugar is not null && lugar.TryGetValue("nombre", out var nombre) ? nombre as string : null;
            var porDefecto = proceso == clsCodigosEtapa.CARGUE
                ? await parametros.EnteroAsync(clsClavesParametros.RADIO_LUGAR_CARGUE_M, RADIO_CARGUE_POR_DEFECTO_M, ct)
                : await parametros.EnteroAsync(clsClavesParametros.RADIO_DESTINO_M, RADIO_DESTINO_POR_DEFECTO_M, ct);
            radio = Numero(lugar, "radio_metros") ?? porDefecto;
            var valvula = await parametros.EnteroAsync(clsClavesParametros.VALVULA_PRECISION_M, VALVULA_POR_DEFECTO_M, ct);
            evaluacion = clsConfirmacionSitio.Evaluar(
                foto.SelloLat, foto.SelloLng, foto.Precision, Numero(lugar, "latitud"), Numero(lugar, "longitud"),
                lugar is not null && lugar.TryGetValue("estado_ubicacion", out var estado) && estado as string == clsEstadoUbicacion.CONFIRMADA, radio, valvula);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un fallo del calculo no toca la captura: la foto queda guardada y sellada y la confirmacion queda sin determinar.
            registro.LogWarning(ex, "No se pudo calcular la confirmación en sitio de la inspección {Inspeccion}", inspeccion.Id);
            evaluacion = new clsEvaluacionSitio(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, null);
        }

        if (actual is clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE && evaluacion.Resultado == clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE)
        {
            return null;
        }

        inspeccion.RegistrarConfirmacionSitio(evaluacion.Resultado, evaluacion.DistanciaM, foto.Id, ahora);
        return evaluacion.Resultado == clsResultadoConfirmacionSitio.FUERA_DE_RANGO && etapa is not null && proceso is not null && evaluacion.DistanciaM is { } distancia
            ? new AvisoFueraDeRango(inspeccion.Id, inspeccion.ViajeId, etapa.VehiculoId, proceso, nombreDelLugar, distancia, radio, foto.Precision)
            : null;
    }

    /// <summary>
    /// Avisa al coordinador de calidad del cliente del vehiculo, por bandeja y push, con el viaje, el lugar y la distancia. Una sola vez por inspeccion (la clave de dedupe es la inspeccion). Quien llama ya
    /// guardo la foto y el resultado: si el aviso falla, no los deshace.
    /// </summary>
    public async Task AvisarAsync(AvisoFueraDeRango aviso, CancellationToken ct = default)
    {
        try
        {
            var vehiculo = await vehiculos.ObtenerPorIdAsync(aviso.VehiculoId, ct);
            if (vehiculo is null)
            {
                return;
            }

            var destinatarios = (await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD], vehiculo.ClienteId, ct)).Select(u => u.Id).ToList();
            if (destinatarios.Count == 0)
            {
                return;
            }

            var viaje = (await viajes.ObtenerPorIdAsync(aviso.ViajeId, ct))?.CodigoMtrack;
            var lugar = aviso.Lugar ?? "el lugar";
            var precision = aviso.PrecisionM is { } p ? $", precisión {p:0.#} m" : string.Empty;
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.FUERA_DE_RANGO,
                    clsUrgenciaNotificacion.ALTA,
                    destinatarios,
                    vehiculo.ClienteId,
                    aviso.ViajeId,
                    aviso.VehiculoId,
                    null,
                    aviso.InspeccionId,
                    $"Fuera del rango: {lugar}",
                    $"La primera foto sellada de {aviso.Proceso} del viaje {viaje ?? "sin código"} del VIN {vehiculo.Vin} se tomó a {aviso.DistanciaM:0} m de {lugar} (rango {aviso.RadioM:0} m{precision}). La inspección continúa sin bloqueo.",
                    $"{clsTipoNotificacion.FUERA_DE_RANGO}:{aviso.InspeccionId}"),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogError(ex, "No se pudo avisar que la inspección {Inspeccion} quedó fuera del rango", aviso.InspeccionId);
        }
    }

    private static double? Numero(ValorCatalogo? valor, string campo) =>
        valor is not null && valor.TryGetValue(campo, out var x) && x is not null ? Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture) : null;
}
