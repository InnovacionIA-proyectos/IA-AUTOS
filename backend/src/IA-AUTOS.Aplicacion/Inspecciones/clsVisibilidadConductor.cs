using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Catalogos;
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
            // Lo que es de la calidad (el nivel) no se ve, y con ello tampoco si esta sin calificar.
            SinCalificar = false,
        };

    /// <summary>
    /// El «no sale» que ve el conductor: sin el hallazgo ni ningun dato del nivel, y con una etiqueta que no revela el nivel («Daño que impide viajar» en lugar de «Daño V1+»; el daño que el reporto sigue diciendolo).
    /// </summary>
    public static NoSaleDto ParaConductor(this NoSaleDto noSale) =>
        noSale with
        {
            HallazgoId = null,
            Motivo = noSale.MotivoCodigo switch
            {
                clsCodigosMotivoNoSale.DANO_V1_PLUS => "Daño que impide viajar",
                clsCodigosMotivoNoSale.DANO_REPORTADO_CONDUCTOR => "Daño reportado por el conductor",
                _ => noSale.Motivo,
            },
        };

    public static ViajeDto ParaConductor(this ViajeDto viaje, IUsuarioActual actual) =>
        actual.EsConductor()
            ? viaje with { Vehiculos = viaje.Vehiculos.Select(v => v.NoSale is null ? v : v with { NoSale = v.NoSale.ParaConductor() }).ToList() }
            : viaje;

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
                        NoSale = e.NoSale?.ParaConductor(),
                        Inspecciones = e.Inspecciones
                            .Select(i => i with { Hallazgos = i.Hallazgos.Select(h => h.ParaConductor()).ToList(), Evidencias = SinSello(i.Evidencias) })
                            .ToList(),
                    })
                    .ToList(),
            };

    /// <summary>Las fotos sin la del sello de quien valida un hallazgo: las de las vistas, las de los hallazgos y las de las constancias se conservan.</summary>
    private static IReadOnlyList<EvidenciaDto> SinSello(IReadOnlyList<EvidenciaDto> evidencias) => evidencias.Where(e => !e.EsSello).ToList();
}
