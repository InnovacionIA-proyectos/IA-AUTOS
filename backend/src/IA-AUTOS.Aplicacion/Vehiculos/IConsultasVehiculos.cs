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
    /// <summary>Coincidencia por fragmento de VIN o marca; maximo 8 resultados. Con <paramref name="soloDeUsuario"/>, solo VIN de los viajes de ese usuario.</summary>
    Task<IReadOnlyList<VehiculoBusquedaDto>> BuscarAsync(string termino, Guid? soloDeUsuario, CancellationToken ct = default);

    /// <summary>Listado paginado: movilizados, pendientes por movilizar o con novedad.</summary>
    Task<(IReadOnlyList<ListadoVehiculoDto> Filas, int Total)> ListarAsync(
        string tipo,
        string? vin,
        Guid? soloDeUsuario,
        int limite,
        int desplazamiento,
        CancellationToken ct = default);

    /// <summary>Inventario completo del VIN o nulo si no existe. Con <paramref name="soloDeUsuario"/>, solo las etapas de los viajes de ese usuario.</summary>
    Task<InventarioDto?> ObtenerInventarioAsync(string vin, Guid? soloDeUsuario, CancellationToken ct = default);
}
