using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// La zona de un hallazgo: cuadrante + lado, del cuadrante del tipo del vehiculo de la inspeccion o del generico. Si el cuadrante lleva lado el lado es obligatorio y si no lo lleva no se envia: la regla
/// sale del catalogo, no de los numeros. 400 con el campo que corresponde si el cuadrante no existe, esta inactivo o el lado no corresponde.
/// </summary>
public sealed class clsResolutorZona(IInspeccionRepository inspecciones)
{
    public async Task<clsZonaDano> ResolverAsync(Guid inspeccionId, int cuadranteNumero, string? lado, CancellationToken ct = default)
    {
        var tipoVehiculo = await inspecciones.ObtenerTipoVehiculoAsync(inspeccionId, ct);
        var llevaLado = await inspecciones.CuadranteLlevaLadoAsync(cuadranteNumero, tipoVehiculo, ct)
            ?? throw clsErroresCampos.De("cuadrante_numero", "El cuadrante indicado no existe o está inactivo");
        if (llevaLado && lado is null)
        {
            throw clsErroresCampos.De("lado", "Este cuadrante lleva lado: indique izquierdo o derecho");
        }

        if (!llevaLado && lado is not null)
        {
            throw clsErroresCampos.De("lado", "Este cuadrante no lleva lado");
        }

        return await inspecciones.ObtenerZonaNumeradaAsync(cuadranteNumero, lado, tipoVehiculo, ct)
            ?? throw clsErroresCampos.De("cuadrante_numero", "El cuadrante indicado no existe o está inactivo");
    }
}
