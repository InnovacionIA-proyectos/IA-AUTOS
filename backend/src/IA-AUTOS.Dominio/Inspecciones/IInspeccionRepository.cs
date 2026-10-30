using IAAutos.Dominio.Catalogos;

namespace IAAutos.Dominio.Inspecciones;

public interface IInspeccionRepository
{
    Task<clsInspeccion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lo que tiene la inspeccion (fotos, ubicaciones, hallazgos sin foto y vistas fotografiadas) para decidir si puede cerrarse.</summary>
    Task<clsResumenCierre> ObtenerResumenCierreAsync(Guid inspeccionId, CancellationToken ct = default);

    /// <summary>True si la etapa tiene una inspeccion de llegada cerrada (de cualquiera de sus versiones).</summary>
    Task<bool> TieneLlegadaCerradaAsync(Guid etapaId, CancellationToken ct = default);

    /// <summary>Las fotos de la inspeccion que no son de una vista, de un hallazgo, de una constancia ni de un sello: las fotos libres, como las de la acomodacion de la segunda inspeccion del cargue.</summary>
    Task<int> ContarFotosLibresAsync(Guid inspeccionId, CancellationToken ct = default);

    /// <summary>True si alguna inspeccion de la etapa tiene una transferencia sin tipo de cierre (abierta): bloquea iniciar la etapa siguiente.</summary>
    Task<bool> TieneTransferenciaSinCierreAsync(Guid etapaId, CancellationToken ct = default);

    /// <summary>La version mas reciente de la inspeccion de ese momento (salida o llegada) de la etapa, o nulo si no la tiene.</summary>
    Task<clsInspeccion?> ObtenerUltimaDeEtapaAsync(Guid etapaId, string momento, CancellationToken ct = default);

    /// <summary>Cuantas novedades del conductor tiene la inspeccion.</summary>
    Task<int> ContarNovedadesAsync(Guid inspeccionId, CancellationToken ct = default);

    /// <summary>Una evidencia por id (por ejemplo, la foto de una constancia de no firma).</summary>
    Task<clsEvidencia?> ObtenerEvidenciaAsync(Guid id, CancellationToken ct = default);

    /// <summary>Id de la version que corrige a esta inspeccion, o nulo si nadie la ha corregido.</summary>
    Task<Guid?> ObtenerCorreccionIdAsync(Guid inspeccionId, CancellationToken ct = default);

    Task<bool> ExisteHallazgoEnInspeccionAsync(Guid hallazgoId, Guid inspeccionId, CancellationToken ct = default);

    /// <summary>
    /// La zona numerada activa de un cuadrante activo, por numero y lado (nulo del 26 en adelante). Con <paramref name="tipoVehiculo"/> prefiere el cuadrante
    /// de ese tipo de vehiculo y, si no existe, el generico (sin tipo). Nulo si no existe.
    /// </summary>
    Task<clsZonaDano?> ObtenerZonaNumeradaAsync(int numero, string? lado, string? tipoVehiculo, CancellationToken ct = default);

    /// <summary>La zona provisional activa con ese codigo (frente, llantas), o nulo.</summary>
    Task<clsZonaDano?> ObtenerZonaProvisionalAsync(string codigo, CancellationToken ct = default);

    /// <summary>Tipo de vehiculo del vehiculo al que pertenece la inspeccion (nulo si no tiene).</summary>
    Task<string?> ObtenerTipoVehiculoAsync(Guid inspeccionId, CancellationToken ct = default);

    /// <summary>
    /// True si el hallazgo es de otra inspeccion YA CERRADA ANTES de que empezara esta, del MISMO vehiculo (mismo VIN), y tiene la misma zona y la misma clase: la condicion para que un
    /// hallazgo (nuevo, o el que se revisa) se marque como preexistente de ese.
    /// </summary>
    Task<bool> EsHallazgoPrevioDelVehiculoAsync(Guid hallazgoOrigenId, Guid inspeccionId, Guid zonaId, string claseDano, CancellationToken ct = default);

    /// <summary>El hallazgo (para modificarlo), o nulo si no existe.</summary>
    Task<clsHallazgo?> ObtenerHallazgoAsync(Guid id, CancellationToken ct = default);

    /// <summary>Los hallazgos de la inspeccion (para recalcular su evidencia).</summary>
    Task<IReadOnlyList<clsHallazgo>> ObtenerHallazgosDeInspeccionAsync(Guid inspeccionId, CancellationToken ct = default);

    /// <summary>De los hallazgos recibidos, los que tienen su foto propia (no cuenta la foto del sello de quien valida).</summary>
    Task<IReadOnlySet<Guid>> HallazgosConFotoAsync(IReadOnlyCollection<Guid> hallazgoIds, CancellationToken ct = default);

    void Agregar(clsInspeccion inspeccion);

    void Agregar(clsHallazgo hallazgo);

    void Agregar(clsEvidencia evidencia);

    void Agregar(clsHallazgoSeguimiento seguimiento);
}
