using IAAutos.Dominio.Comun;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia;

/// <summary>
/// Unidad de trabajo sobre el DbContext. Traduce los errores de SQL Server (duplicados, claves foraneas,
/// restricciones CHECK y triggers de inmutabilidad) en excepciones de dominio con los mensajes de la API.
/// </summary>
internal sealed class clsUnidadDeTrabajo(clsAppDbContext contexto) : IUnidadDeTrabajo
{
    private const int ERROR_UNICO_INDICE = 2601;
    private const int ERROR_UNICO_RESTRICCION = 2627;
    private const int ERROR_CONFLICTO_RESTRICCION = 547;
    private const int ERROR_TRIGGER_INSPECCION_CERRADA = 51000;
    private const int ERROR_TRIGGER_HALLAZGO_EN_CERRADA = 51001;
    private const int ERROR_TRIGGER_EVIDENCIA_EN_CERRADA = 51002;
    private const int ERROR_TRIGGER_EVIDENCIA_NO_SE_BORRA = 51003;

    // Mensajes por restriccion: coinciden con los de la API original.
    private static readonly (string Restriccion, string Mensaje)[] CONFLICTOS_UNICOS =
    [
        ("UX_tblUsuarios_Email", "Ya existe un usuario con ese correo"),
        ("UQ_tblVehiculos_Vin", "Ya existe un vehículo con ese VIN"),
        ("UQ_tblEtapas_Viaje_Vehiculo_Orden", "Ya existe una etapa con ese orden para el vehículo en el viaje"),
        ("UX_tblViajes_CodigoMtrack", "Ya existe un viaje con ese código MTrack"),
        ("UQ_tblViajeVehiculos_Viaje_Vehiculo", "El VIN ya está en el viaje"),
        ("UX_tblUsuarios_Cedula", "Ya existe un usuario con esa cédula"),
        ("UX_tblInspecciones_Etapa_Momento_Original", "Esta etapa ya tiene una inspección de ese momento: para cambiarla cree una corrección"),
        ("UX_tblInspecciones_Corrige", "Esa inspección ya fue corregida: corrija la última versión"),
        ("UQ_tblTransferencias_Inspeccion", "Esta inspección ya tiene una transferencia de custodia"),
        ("UQ_tblOrdenesSofasa_SourceId", "Ya existe una orden con ese identificador de origen"),
        // Catalogos maestros: el codigo es unico dentro de cada catalogo.
        ("_Codigo", "Ya existe un valor con ese código en el catálogo"),
        ("UQ_tblCuadrantes_Tipo_Numero", "Ya existe un cuadrante con ese número para ese tipo de vehículo"),
        ("UQ_tblCuadranteVistas_Cuadrante_Vista", "El cuadrante ya tiene un polígono en esa vista"),
    ];

    // Borrar un valor de catalogo que algo todavia referencia: se responde que esta en uso y que solo puede inactivarse.
    private static readonly string[] CATALOGOS_EN_USO =
    [
        "FK_tblHallazgos_ClaseDano",
        "FK_tblHallazgos_TamanoDano",
        "FK_tblEtapas_EtapaCatalogo",
        "FK_tblEtapas_PuntoCustodia",
        "FK_tblViajes_TipoUnidad",
        "FK_tblViajes_Origen",
        "FK_tblViajes_Destino",
        "FK_tblCuadranteVistas_Vista",
        "FK_tblUsuarios_Cliente",
        "FK_tblVehiculos_Cliente",
        "FK_tblEvidencias_Vista",
    ];

    private static readonly (string Restriccion, string Mensaje)[] REFERENCIAS_INVALIDAS =
    [
        ("FK_tblEtapas_EtapaCatalogo", "La etapa no existe en el catálogo"),
        ("FK_tblEtapas_PuntoCustodia", "El punto de custodia no existe en el catálogo"),
        ("FK_tblEtapas_Viaje", "El viaje no existe"),
        ("FK_tblEtapas_", "El vehículo no existe"),
        ("FK_tblViajes_TipoUnidad", "La unidad transportadora no existe en el catálogo"),
        ("FK_tblViajes_Origen", "El origen no existe en el catálogo de lugares"),
        ("FK_tblViajes_Destino", "El destino no existe en el catálogo de lugares"),
        ("FK_tblUsuarios_Cliente", "El cliente no existe en el catálogo"),
        ("FK_tblVehiculos_Cliente", "El cliente no existe en el catálogo"),
        ("FK_tblViajes_", "El conductor, el inspector o quien crea el viaje no existe"),
        ("FK_tblViajeVehiculos_Viaje", "El viaje no existe"),
        ("FK_tblViajeVehiculos_", "El vehículo no existe"),
        ("FK_tblInspecciones_Viaje", "El viaje no existe"),
        ("FK_tblInspecciones_Corrige", "La inspección que se corrige no existe"),
        ("FK_tblInspecciones_", "La etapa o el usuario no existen"),
        ("FK_tblTransferencias_Testigo", "El testigo no existe"),
        ("FK_tblTransferencias_Constancia", "La foto de la constancia no existe"),
        ("FK_tblTransferencias_", "Algún usuario (entrega/recibe) no existe"),
        ("FK_tblAsignaciones_", "Referencia inválida (vehículo o usuario)"),
        ("FK_tblEvidencias_Vista", "La vista de foto no existe en el catálogo"),
        ("FK_tblEvidencias_", "El hallazgo indicado no pertenece a esta inspección"),
        ("FK_tblHallazgos_ClaseDano", "La clase de daño no existe en el catálogo"),
        ("FK_tblHallazgos_TamanoDano", "El tamaño de daño no existe en el catálogo"),
        ("FK_tblHallazgos_", "La inspección indicada no existe"),
        ("FK_tblCuadranteVistas_Cuadrante", "El cuadrante no existe"),
        ("FK_tblCuadranteVistas_Vista", "La vista no existe"),
    ];

    public async Task<int> GuardarCambiosAsync(CancellationToken ct = default)
    {
        try
        {
            return await contexto.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql)
        {
            throw Traducir(sql) ?? throw new InvalidOperationException("Error de base de datos no controlado", ex);
        }
    }

    public void DescartarCambios() => contexto.ChangeTracker.Clear();

    private static clsExcepcionDominio? Traducir(SqlException sql)
    {
        switch (sql.Number)
        {
            case ERROR_UNICO_INDICE or ERROR_UNICO_RESTRICCION:
                foreach (var (restriccion, mensaje) in CONFLICTOS_UNICOS)
                {
                    if (sql.Message.Contains(restriccion, StringComparison.OrdinalIgnoreCase))
                    {
                        return new clsConflictoException(mensaje);
                    }
                }

                return new clsConflictoException("Ya existe un registro con esos datos");

            case ERROR_CONFLICTO_RESTRICCION:
                if (sql.Message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase))
                {
                    return new clsValidacionException("Los datos no cumplen las restricciones del modelo");
                }

                if (sql.Message.Contains("DELETE statement", StringComparison.OrdinalIgnoreCase))
                {
                    if (CATALOGOS_EN_USO.Any(fk => sql.Message.Contains(fk, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new clsConflictoException("El valor está en uso y no se puede borrar: solo puede inactivarlo");
                    }

                    return new clsConflictoException("El registro tiene operaciones asociadas y no puede eliminarse");
                }

                foreach (var (prefijo, mensaje) in REFERENCIAS_INVALIDAS)
                {
                    if (sql.Message.Contains(prefijo, StringComparison.OrdinalIgnoreCase))
                    {
                        return new clsEntidadNoEncontradaException(mensaje);
                    }
                }

                return new clsEntidadNoEncontradaException("Referencia inválida");

            case ERROR_TRIGGER_INSPECCION_CERRADA or ERROR_TRIGGER_HALLAZGO_EN_CERRADA or ERROR_TRIGGER_EVIDENCIA_EN_CERRADA:
                return new clsConflictoException("La inspección está cerrada y es inmutable; no admite cambios");

            case ERROR_TRIGGER_EVIDENCIA_NO_SE_BORRA:
                return new clsConflictoException("El registro es evidencia y no se puede borrar");

            default:
                return null;
        }
    }
}
