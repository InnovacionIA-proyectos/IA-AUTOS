using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// La zona de un hallazgo: la numerada (cuadrante + lado, del cuadrante del tipo del vehiculo de la inspeccion o del generico) o la provisional con ese codigo. 400 con el campo que
/// corresponde si no existe o esta inactiva.
/// </summary>
public sealed class clsResolutorZona(IInspeccionRepository inspecciones)
{
    public async Task<clsZonaDano> ResolverAsync(Guid inspeccionId, int? cuadranteNumero, string? lado, string? zonaProvisional, CancellationToken ct = default)
    {
        if (cuadranteNumero is { } numero)
        {
            var tipoVehiculo = await inspecciones.ObtenerTipoVehiculoAsync(inspeccionId, ct);
            return await inspecciones.ObtenerZonaNumeradaAsync(numero, lado, tipoVehiculo, ct)
                ?? throw clsErroresCampos.De("cuadrante_numero", "El cuadrante indicado no existe o está inactivo");
        }

        return await inspecciones.ObtenerZonaProvisionalAsync(zonaProvisional!, ct)
            ?? throw clsErroresCampos.De("zona_provisional", "La zona provisional indicada no existe o está inactiva");
    }
}
