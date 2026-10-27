using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// El conductor ve, en modo lectura, los hallazgos de las inspecciones de sus viajes (el inventario del VIN solo trae las etapas de sus viajes y el informe de otro viaje responde 403): la zona, la clase, el
/// tamano, el tipo de novedad, el origen, si es preexistente, la fecha y su foto. No ve lo que es del seguimiento y de la calidad: el nivel de severidad, el estado de seguimiento, si la evidencia esta completa
/// o que le falta, la revision, la nota ni el hallazgo previo al que se unio, y tampoco la foto del sello de quien valida. Los demas roles no cambian.
/// </summary>
public static class clsVisibilidadConductor
{
    /// <summary>True si el usuario es un conductor: lo que lee de los hallazgos se limita a lo que ve en lectura.</summary>
    public static bool EsConductor(this IUsuarioActual actual) => actual.Rol == clsRol.CONDUCTOR;

    public static HallazgoDto ParaConductor(this HallazgoDto hallazgo) =>
        hallazgo with
        {
            NivelSeveridad = null,
            NivelNombre = null,
            NivelAccion = null,
            Nota = null,
            HallazgoOrigenId = null,
            ConfirmadoPor = null,
            EstadoSeguimiento = null,
            EvidenciaCompleta = null,
            EvidenciaFaltante = null,
            RevisadoPor = null,
            RevisadoAt = null,
        };

    public static InformeDetalleDto ParaConductor(this InformeDetalleDto informe, IUsuarioActual actual) =>
        actual.EsConductor()
            ? informe with { Hallazgos = informe.Hallazgos.Select(h => h.ParaConductor()).ToList(), Evidencias = SinSello(informe.Evidencias) }
            : informe;

    public static InventarioDto ParaConductor(this InventarioDto inventario, IUsuarioActual actual) =>
        !actual.EsConductor()
            ? inventario
            : inventario with
            {
                Etapas = inventario.Etapas
                    .Select(e => e with
                    {
                        Inspecciones = e.Inspecciones
                            .Select(i => i with { Hallazgos = i.Hallazgos.Select(h => h.ParaConductor()).ToList(), Evidencias = SinSello(i.Evidencias) })
                            .ToList(),
                    })
                    .ToList(),
            };

    /// <summary>Las fotos sin la del sello de quien valida un hallazgo: las de las vistas, las de los hallazgos y las de las constancias se conservan.</summary>
    private static IReadOnlyList<EvidenciaDto> SinSello(IReadOnlyList<EvidenciaDto> evidencias) => evidencias.Where(e => !e.EsSello).ToList();
}
