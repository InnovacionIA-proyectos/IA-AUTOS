using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;

namespace IAAutos.Aplicacion.Vehiculos;

public static class clsTipoListado
{
    public const string MOVILIZADOS = "movilizados";
    public const string PENDIENTES = "pendientes";
    public const string NOVEDAD = "novedad";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { MOVILIZADOS, PENDIENTES, NOVEDAD };
}

/// <summary>Consultas de solo lectura sobre vehiculos (listados del tablero, busqueda e inventario por VIN).</summary>
public interface IConsultasVehiculos
{
    /// <summary>Coincidencia por fragmento de VIN o marca; maximo 8 resultados.</summary>
    Task<IReadOnlyList<VehiculoBusquedaDto>> BuscarAsync(string termino, CancellationToken ct = default);

    /// <summary>Listado paginado: movilizados, pendientes por movilizar o con novedad.</summary>
    Task<(IReadOnlyList<ListadoVehiculoDto> Filas, int Total)> ListarAsync(
        string tipo,
        string? vin,
        int limite,
        int desplazamiento,
        CancellationToken ct = default);

    /// <summary>Inventario completo del VIN o nulo si no existe.</summary>
    Task<InventarioDto?> ObtenerInventarioAsync(string vin, CancellationToken ct = default);
}
