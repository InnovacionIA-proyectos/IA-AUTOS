using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Comun;

/// <summary>
/// El estado de cada registro tal como se guarda en el <c>antes</c> y el <c>despues</c> de la auditoria: solo los campos que describen el cambio, nunca contrasenas, PIN,
/// tokens ni firmas.
/// </summary>
public static class clsInstantaneasAuditoria
{
    public static object De(clsInspeccion i) => new
    {
        i.Momento,
        i.Estado,
        i.Version,
        i.ViajeId,
        i.EtapaId,
        i.UsuarioId,
        i.CorrigeInspeccionId,
        i.Fecha,
        i.Lat,
        i.Lng,
        i.ConductorNombre,
        i.Placa,
        i.Trailer,
        i.Notas,
        i.CerradaAt,
    };

    public static object De(clsHallazgo h) => new
    {
        h.InspeccionId,
        h.Origen,
        h.ParteVehiculo,
        h.ClaseDano,
        h.TamanoDano,
        h.NivelSeveridad,
        h.ZonaId,
        h.TipoNovedad,
        h.Nota,
        h.EsPreexistente,
        h.EstadoSeguimiento,
        h.EvidenciaCompleta,
        h.ConfirmadoPor,
        h.RevisadoPor,
        h.RevisadoAt,
    };

    public static object De(clsEvidencia e) => new
    {
        e.InspeccionId,
        e.HallazgoId,
        e.VistaId,
        e.StoragePath,
        e.HashSha256,
        e.SelloFecha,
        e.SelloLat,
        e.SelloLng,
        e.EsConstancia,
        e.EsSello,
    };

    public static object De(clsTransferenciaCustodia t) => new
    {
        t.InspeccionId,
        t.EntregaUsuarioId,
        t.RecibeUsuarioId,
        entrega_firmada = t.FirmaEntregaAt,
        recibe_firmada = t.FirmaRecibeAt,
        t.FirmadaAt,
        t.TipoCierre,
        t.ReservasTexto,
        t.NoFirmaParte,
        t.MotivoNoFirma,
        t.TestigoUsuarioId,
        t.EvidenciaConstanciaId,
    };

    public static object De(clsViaje v) => new
    {
        v.CodigoMtrack,
        v.Estado,
        v.TipoUnidadId,
        v.Placa,
        v.Trailer,
        v.ConductorId,
        v.InspectorId,
        v.OrigenId,
        v.DestinoId,
    };

    public static object De(clsVehiculo v) => new
    {
        v.Vin,
        v.Marca,
        v.Modelo,
        v.Color,
        v.Operacion,
        v.ClienteId,
    };

    /// <summary>El usuario sin credenciales: ni hash de contrasena, ni PIN, ni tokens.</summary>
    public static object De(clsUsuario u) => new
    {
        u.Nombre,
        u.Email,
        u.Rol,
        u.SubtipoInspector,
        u.Cedula,
        u.TipoCredencial,
        u.Activo,
        u.ClienteId,
        u.DebeCambiarPin,
    };
}
