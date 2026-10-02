using FluentValidation;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Vehiculos;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Vehiculos;

/// <summary>Carga masiva de VINs, busqueda, listados del tablero e inventario por VIN.</summary>
public sealed class clsVehiculosService(
    IVehiculoRepository vehiculos,
    IEtapaRepository etapas,
    IPuntoCustodiaRepository puntos,
    IConsultasVehiculos consultas,
    IGeneradorPdfInventario generadorPdf,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IReloj reloj,
    IValidator<CargarVinsRequest> validadorCarga,
    ILogger<clsVehiculosService> registro)
{
    public const int TAMANO_PAGINA = 500;

    /// <summary>
    /// Carga masiva de vehiculos con su ruta Cargue, Transporte, Descargue. Cada VIN se inserta (o se actualiza si ya existe)
    /// y recibe las etapas que le falten. Re-subir el mismo archivo no duplica. Los errores se reportan por VIN sin
    /// detener el resto del lote.
    /// </summary>
    public async Task<ResultadoCargaDto> CargarVinsAsync(CargarVinsRequest solicitud, CancellationToken ct = default)
    {
        await validadorCarga.ValidarOLanzarAsync(solicitud, ct);
        var items = solicitud.Items!;
        var materializador = new clsMaterializadorVehiculos(vehiculos, etapas, puntos, reloj);
        await materializador.PrepararAsync(items.Select(i => clsVin.Normalizar(i.Vin!)).ToList(), ct);

        int creados = 0, actualizados = 0;
        var errores = new List<ErrorCargaDto>();

        foreach (var item in items)
        {
            var vin = clsVin.Normalizar(item.Vin!);
            try
            {
                var existente = await materializador.ObtenerAsync(vin, ct);
                var esNuevo = existente is null;
                var vehiculo = existente;
                if (vehiculo is null)
                {
                    vehiculo = clsVehiculo.Crear(vin, item.Marca!, item.Modelo, item.Color, item.Operacion!, reloj.Ahora);
                    vehiculos.Agregar(vehiculo);
                }
                else
                {
                    vehiculo.ActualizarDesdeCarga(item.Marca!, item.Modelo, item.Color, item.Operacion!, reloj.Ahora);
                }

                await materializador.AgregarRutaFaltanteAsync(vehiculo, esNuevo, ct);
                await unidadDeTrabajo.GuardarCambiosAsync(ct);

                if (esNuevo)
                {
                    materializador.Registrar(vehiculo);
                    creados++;
                }
                else
                {
                    actualizados++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unidadDeTrabajo.DescartarCambios();
                materializador.InvalidarCache();
                errores.Add(new ErrorCargaDto(vin, MensajeSeguro(ex, vin)));
            }
        }

        auditor.Registrar("vehiculos.cargar", "vehiculos");
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new ResultadoCargaDto(items.Count, creados, actualizados, errores.Count, errores);
    }

    public async Task<IReadOnlyList<VehiculoBusquedaDto>> BuscarAsync(string? termino, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(termino) ? [] : await consultas.BuscarAsync(termino.Trim(), ct);

    public async Task<ListadoPaginadoDto> ListarAsync(string? tipo, string? vin, int pagina, CancellationToken ct = default)
    {
        var tipoFinal = tipo ?? clsTipoListado.MOVILIZADOS;
        if (!clsTipoListado.VALORES.Contains(tipoFinal))
        {
            throw new clsValidacionException(
                "Datos inválidos",
                new Dictionary<string, object>
                {
                    ["formErrors"] = Array.Empty<string>(),
                    ["fieldErrors"] = new Dictionary<string, string[]> { ["tipo"] = [$"Valor inválido. Permitidos: {string.Join(", ", clsTipoListado.VALORES.Order())}"] },
                });
        }

        var paginaFinal = Math.Max(pagina, 1);
        var (filas, total) = await consultas.ListarAsync(
            tipoFinal,
            string.IsNullOrWhiteSpace(vin) ? null : vin.Trim(),
            TAMANO_PAGINA,
            (paginaFinal - 1) * TAMANO_PAGINA,
            ct);
        return new ListadoPaginadoDto(filas, total, paginaFinal, TAMANO_PAGINA);
    }

    public async Task<InventarioDto> ObtenerInventarioAsync(string vin, CancellationToken ct = default) =>
        await consultas.ObtenerInventarioAsync(vin, ct)
            ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");

    /// <summary>Inventario del VIN en PDF (soporte de rechazo).</summary>
    public async Task<byte[]> ObtenerInventarioPdfAsync(string vin, CancellationToken ct = default) =>
        generadorPdf.Generar(await ObtenerInventarioAsync(vin, ct));

    private string MensajeSeguro(Exception ex, string vin)
    {
        if (ex is clsExcepcionDominio dominio)
        {
            return dominio.Message;
        }

        registro.LogWarning(ex, "No se pudo procesar el VIN {Vin} en la carga masiva", vin);
        return "No se pudo procesar el VIN";
    }
}
