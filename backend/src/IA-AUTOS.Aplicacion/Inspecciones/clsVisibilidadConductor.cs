using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// El conductor responde por lo que pasa en ruta: no ve los hallazgos que registraron otros (el inspector, por ejemplo). En toda inspeccion que el no creo, el informe y el inventario le
/// llegan con la lista de hallazgos vacia y sin las fotos de los hallazgos (ni las del sello de quien los valida). En las que creo (sus novedades) lo ve todo.
/// </summary>
public static class clsVisibilidadConductor
{
    /// <summary>True si el usuario es un conductor y la inspeccion no la creo el.</summary>
    public static bool OcultaHallazgos(this IUsuarioActual actual, Guid creadaPor) => actual.Rol == clsRol.CONDUCTOR && creadaPor != actual.Id;

    public static InformeDetalleDto ParaConductor(this InformeDetalleDto informe, IUsuarioActual actual) =>
        actual.OcultaHallazgos(informe.UsuarioId)
            ? informe with { Hallazgos = [], Evidencias = SinFotosDeHallazgos(informe.Evidencias) }
            : informe;

    public static InventarioDto ParaConductor(this InventarioDto inventario, IUsuarioActual actual) =>
        inventario with
        {
            Etapas = inventario.Etapas
                .Select(e => e with
                {
                    Inspecciones = e.Inspecciones
                        .Select(i => actual.OcultaHallazgos(i.UsuarioId) ? i with { Hallazgos = [], Evidencias = SinFotosDeHallazgos(i.Evidencias) } : i)
                        .ToList(),
                })
                .ToList(),
        };

    private static IReadOnlyList<EvidenciaDto> SinFotosDeHallazgos(IReadOnlyList<EvidenciaDto> evidencias) =>
        evidencias.Where(e => e.HallazgoId is null && !e.EsSello).ToList();
}
