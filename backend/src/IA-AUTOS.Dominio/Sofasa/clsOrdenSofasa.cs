using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Sofasa;

/// <summary>Datos de una orden de carga de Sofasa ya depurados (vacios convertidos en nulos, fechas interpretadas).</summary>
public sealed record clsDatosOrdenSofasa(
    long SourceId,
    string? StatusCode,
    DateTimeOffset? DatetimeEvent,
    string? OrdenNumber,
    string? OrdenSap,
    string? OriginCedi,
    string? Vin,
    string? Make,
    string? ModelDescription,
    string? DealerCode,
    string? DestinationCity,
    string? DestinationAddress,
    DateTimeOffset? LoadingDate,
    DateTimeOffset? Appointment,
    string? LoadingDock,
    decimal? Weight);

/// <summary>Orden de carga ingerida desde el SQL Server de Sofasa por el ETL. Idempotente por SourceId.</summary>
public sealed class clsOrdenSofasa : clsEntidadBase
{
    private clsOrdenSofasa()
    {
    }

    /// <summary>[ID] de la fuente: clave de idempotencia en cada corrida del ETL.</summary>
    public long SourceId { get; private set; }

    public string? StatusCode { get; private set; }

    public DateTimeOffset? DatetimeEvent { get; private set; }

    public string? OrdenNumber { get; private set; }

    public string? OrdenSap { get; private set; }

    public string? OriginCedi { get; private set; }

    public string? Vin { get; private set; }

    public string? Make { get; private set; }

    public string? ModelDescription { get; private set; }

    public string? DealerCode { get; private set; }

    public string? DestinationCity { get; private set; }

    public string? DestinationAddress { get; private set; }

    public DateTimeOffset? LoadingDate { get; private set; }

    public DateTimeOffset? Appointment { get; private set; }

    public string? LoadingDock { get; private set; }

    public decimal? Weight { get; private set; }

    /// <summary>Vehiculo materializado a partir del VIN (si aplica).</summary>
    public Guid? VehiculoId { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public static clsOrdenSofasa Crear(clsDatosOrdenSofasa datos, Guid? vehiculoId, DateTimeOffset ahora)
    {
        var orden = new clsOrdenSofasa { SourceId = datos.SourceId };
        orden.Inicializar(ahora);
        orden.Aplicar(datos, vehiculoId, ahora);
        return orden;
    }

    /// <summary>Re-ingesta: reemplaza los datos de la orden; el vehiculo se conserva si no llega uno nuevo.</summary>
    public void Aplicar(clsDatosOrdenSofasa datos, Guid? vehiculoId, DateTimeOffset ahora)
    {
        StatusCode = datos.StatusCode;
        DatetimeEvent = datos.DatetimeEvent;
        OrdenNumber = datos.OrdenNumber;
        OrdenSap = datos.OrdenSap;
        OriginCedi = datos.OriginCedi;
        Vin = datos.Vin;
        Make = datos.Make;
        ModelDescription = datos.ModelDescription;
        DealerCode = datos.DealerCode;
        DestinationCity = datos.DestinationCity;
        DestinationAddress = datos.DestinationAddress;
        LoadingDate = datos.LoadingDate;
        Appointment = datos.Appointment;
        LoadingDock = datos.LoadingDock;
        Weight = datos.Weight;
        VehiculoId = vehiculoId ?? VehiculoId;
        SyncedAt = ahora;
        Tocar(ahora);
    }
}
