using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>Unidad transportada, identificada por VIN.</summary>
public sealed class clsVehiculo : clsEntidadBase
{
    private clsVehiculo()
    {
    }

    public string Vin { get; private set; } = string.Empty;

    public string Marca { get; private set; } = string.Empty;

    public string? TipoVehiculo { get; private set; }

    public string? Modelo { get; private set; }

    public string? Chasis { get; private set; }

    public string? Color { get; private set; }

    public string Operacion { get; private set; } = string.Empty;

    /// <summary>Cliente (empresa) dueno del vehiculo.</summary>
    public Guid ClienteId { get; private set; }

    /// <summary>Alta desde carga masiva. El VIN es laxo (acepta numeros de chasis de longitud variable).</summary>
    public static clsVehiculo Crear(string vin, string marca, string? modelo, string? color, string operacion, Guid clienteId, DateTimeOffset ahora)
    {
        if (clienteId == Guid.Empty)
        {
            throw new clsValidacionException("El cliente es requerido");
        }

        clsValidador.EnCatalogo(operacion, clsOperacion.VALORES, "operacion");
        var vehiculo = new clsVehiculo
        {
            Vin = clsVin.Normalizar(clsValidador.Requerido(vin, "VIN requerido")),
            Marca = clsValidador.Requerido(marca, "Marca requerida"),
            Modelo = clsValidador.Opcional(modelo),
            Color = clsValidador.Opcional(color),
            Operacion = operacion,
            ClienteId = clienteId,
        };
        vehiculo.Inicializar(ahora);
        return vehiculo;
    }

    /// <summary>Alta desde una orden Sofasa: operacion fija "nacional".</summary>
    public static clsVehiculo CrearDesdeSofasa(string vin, string? marca, string? modelo, Guid clienteId, DateTimeOffset ahora) =>
        Crear(vin, clsValidador.Opcional(marca) ?? "Sofasa", modelo, null, clsOperacion.NACIONAL, clienteId, ahora);

    /// <summary>Re-carga de un VIN existente: marca y operacion se reemplazan; modelo y color se conservan si no llegan.</summary>
    public void ActualizarDesdeCarga(string marca, string? modelo, string? color, string operacion, DateTimeOffset ahora)
    {
        clsValidador.EnCatalogo(operacion, clsOperacion.VALORES, "operacion");
        Marca = clsValidador.Requerido(marca, "Marca requerida");
        Modelo = clsValidador.Opcional(modelo) ?? Modelo;
        Color = clsValidador.Opcional(color) ?? Color;
        Operacion = operacion;
        Tocar(ahora);
    }

    /// <summary>Re-ingesta Sofasa: la marca se reemplaza, el modelo se conserva si no llega y la operacion no cambia.</summary>
    public void ActualizarDesdeSofasa(string? marca, string? modelo, DateTimeOffset ahora)
    {
        Marca = clsValidador.Opcional(marca) ?? "Sofasa";
        Modelo = clsValidador.Opcional(modelo) ?? Modelo;
        Tocar(ahora);
    }
}
