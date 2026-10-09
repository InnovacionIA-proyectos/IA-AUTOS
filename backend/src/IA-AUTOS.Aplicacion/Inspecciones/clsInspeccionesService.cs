using System.Globalization;
using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Vehiculos;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Consultas de solo lectura de inspecciones (listado e informe detallado).</summary>
public interface IConsultasInspecciones
{
    Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspecciones filtros, int limite, CancellationToken ct = default);

    Task<InformeDetalleDto?> ObtenerInformeAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Flujo de campo: crear la inspeccion de una etapa, agregar hallazgos y evidencias, registrar la transferencia de custodia
/// (firma parcial acumulada) y cerrarla como evidencia inmutable.
/// </summary>
public sealed class clsInspeccionesService(
    IInspeccionRepository inspecciones,
    ITransferenciaCustodiaRepository transferencias,
    IConsultasInspecciones consultas,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    ICatalogosLectura catalogos,
    IOptions<clsOpcionesSeguridad> seguridad,
    IValidator<CrearInspeccionRequest> validadorInspeccion,
    IValidator<CrearHallazgoRequest> validadorHallazgo,
    IValidator<CrearEvidenciaRequest> validadorEvidencia,
    IValidator<FirmarTransferenciaRequest> validadorTransferencia)
{
    public const int LIMITE_LISTADO = 200;

    public async Task<InspeccionDto> CrearAsync(CrearInspeccionRequest solicitud, CancellationToken ct = default)
    {
        await validadorInspeccion.ValidarOLanzarAsync(solicitud, ct);
        // El usuario siempre sale del token: el cliente no puede crear informes a nombre de otro.
        var inspeccion = clsInspeccion.Crear(
            solicitud.EtapaId!.Value,
            actual.IdObligatorio(),
            solicitud.Momento!,
            solicitud.Fecha!.Value,
            solicitud.Lat,
            solicitud.Lng,
            null,
            solicitud.ConductorNombre,
            solicitud.Placa,
            solicitud.Trailer,
            solicitud.Notas,
            reloj.Ahora);
        inspecciones.Agregar(inspeccion);
        auditor.Registrar("inspeccion.crear", "inspecciones", inspeccion.Id);

        // Etapa o usuario inexistentes (404) y un segundo informe en la misma etapa (409) se traducen al confirmar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.AInspeccion(inspeccion);
    }

    public async Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspeccionesRequest solicitud, CancellationToken ct = default) =>
        await consultas.ListarAsync(await ParsearFiltrosAsync(solicitud, ct), LIMITE_LISTADO, ct);

    public async Task<InformeDetalleDto> ObtenerInformeAsync(Guid id, CancellationToken ct = default) =>
        await consultas.ObtenerInformeAsync(id, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {id} no encontrada");

    public async Task<HallazgoDto> AgregarHallazgoAsync(Guid inspeccionId, CrearHallazgoRequest solicitud, CancellationToken ct = default)
    {
        await ValidarHallazgoAsync(solicitud, ct);
        var inspeccion = await ObtenerParaEscrituraAsync(inspeccionId, ct);
        inspeccion.AsegurarEditable();

        var hallazgo = clsHallazgo.Crear(
            inspeccionId,
            solicitud.ParteVehiculo!,
            solicitud.ClaseDano!,
            solicitud.TamanoDano!,
            solicitud.CoordsDiagrama is null ? null : new clsCoordsDiagrama(solicitud.CoordsDiagrama.X, solicitud.CoordsDiagrama.Y),
            reloj.Ahora);
        inspecciones.Agregar(hallazgo);
        auditor.Registrar("hallazgo.crear", "hallazgos", hallazgo.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.AHallazgo(hallazgo);
    }

    public async Task<EvidenciaDto> AgregarEvidenciaAsync(Guid inspeccionId, CrearEvidenciaRequest solicitud, CancellationToken ct = default)
    {
        await validadorEvidencia.ValidarOLanzarAsync(solicitud, ct);
        var inspeccion = await ObtenerParaEscrituraAsync(inspeccionId, ct);
        inspeccion.AsegurarEditable();

        if (solicitud.HallazgoId is { } hallazgoId
            && !await inspecciones.ExisteHallazgoEnInspeccionAsync(hallazgoId, inspeccionId, ct))
        {
            throw new clsEntidadNoEncontradaException("El hallazgo indicado no pertenece a esta inspección");
        }

        var evidencia = clsEvidencia.Crear(
            inspeccionId,
            solicitud.HallazgoId,
            solicitud.StoragePath!,
            solicitud.SelloFecha!.Value,
            solicitud.SelloLat,
            solicitud.SelloLng,
            reloj.Ahora);
        inspecciones.Agregar(evidencia);
        auditor.Registrar("evidencia.crear", "evidencias", evidencia.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.AEvidencia(evidencia);
    }

    /// <summary>
    /// Registra la transferencia de custodia. Admite firma parcial (entrega o recibe por separado, incluso desde dispositivos
    /// distintos): si ya existe, acumula las firmas y sella la fecha solo cuando estan ambas. Se puede firmar tambien despues
    /// de cerrar la inspeccion (firma sobre el ultimo informe); una vez firmada por ambas partes es inmutable.
    /// </summary>
    public async Task<TransferenciaDto> FirmarTransferenciaAsync(Guid inspeccionId, FirmarTransferenciaRequest solicitud, CancellationToken ct = default)
    {
        await validadorTransferencia.ValidarOLanzarAsync(solicitud, ct);
        _ = await inspecciones.ObtenerPorIdAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");

        var existente = await transferencias.ObtenerPorInspeccionAsync(inspeccionId, ct);
        clsTransferenciaCustodia transferencia;
        if (existente is null)
        {
            transferencia = clsTransferenciaCustodia.Crear(
                inspeccionId, solicitud.EntregaUsuarioId!.Value, solicitud.RecibeUsuarioId!.Value, solicitud.FirmaEntrega, solicitud.FirmaRecibe, reloj.Ahora);
            transferencias.Agregar(transferencia);
        }
        else
        {
            transferencia = existente;
            transferencia.AplicarFirma(
                solicitud.EntregaUsuarioId!.Value, solicitud.RecibeUsuarioId!.Value, solicitud.FirmaEntrega, solicitud.FirmaRecibe, reloj.Ahora);
        }

        auditor.Registrar("transferencia.firmar", "transferencias_custodia", transferencia.Id);

        // Un usuario inexistente se traduce a 404 al confirmar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.ATransferencia(transferencia);
    }

    /// <summary>
    /// Cierra la inspeccion como evidencia inmutable. Las fotos solo son obligatorias si hay una novedad (hallazgo) y toda
    /// foto presente debe estar geolocalizada.
    /// </summary>
    public async Task<CerrarInspeccionResponse> CerrarAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var inspeccion = await ObtenerParaEscrituraAsync(inspeccionId, ct);
        var resumen = await inspecciones.ObtenerResumenCierreAsync(inspeccionId, ct);
        inspeccion.Cerrar(resumen, reloj.Ahora);
        auditor.Registrar("inspeccion.cerrar", "inspecciones", inspeccion.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new CerrarInspeccionResponse(
            clsMapeadorInspecciones.AInspeccion(inspeccion),
            new ResumenCierreDto(resumen.TotalEvidencias, resumen.TotalHallazgos));
    }

    /// <summary>Obtiene la inspeccion (404 si no existe) y verifica que el usuario pueda modificarla.</summary>
    private async Task<clsInspeccion> ObtenerParaEscrituraAsync(Guid inspeccionId, CancellationToken ct)
    {
        var inspeccion = await inspecciones.ObtenerPorIdAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");

        // Solo quien creo el informe (o un administrador) le agrega datos o lo cierra.
        if (seguridad.Value.ValidarPropiedadInspeccion && !actual.EsAdministrador() && inspeccion.UsuarioId != actual.Id)
        {
            throw new clsProhibidoException("No tiene permisos sobre esta inspección");
        }

        return inspeccion;
    }

    /// <summary>
    /// Valida el cuerpo y ademas que la clase y el tamano existan en el catalogo y esten activos (un valor inactivo ya no se ofrece para
    /// hallazgos nuevos). Los errores de ambas validaciones se devuelven juntos.
    /// </summary>
    private async Task ValidarHallazgoAsync(CrearHallazgoRequest solicitud, CancellationToken ct)
    {
        var deCatalogo = new Dictionary<string, string[]>();
        if (!string.IsNullOrWhiteSpace(solicitud.ClaseDano)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.CLASES_DANO, soloActivos: true, ct)).Contains(solicitud.ClaseDano))
        {
            deCatalogo["clase_dano"] = ["Clase de daño inválida"];
        }

        if (!string.IsNullOrWhiteSpace(solicitud.TamanoDano)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.TAMANOS_DANO, soloActivos: true, ct)).Contains(solicitud.TamanoDano))
        {
            deCatalogo["tamano_dano"] = ["Tamaño de daño inválido"];
        }

        try
        {
            await validadorHallazgo.ValidarOLanzarAsync(solicitud, ct);
        }
        catch (clsValidacionException ex) when (deCatalogo.Count > 0 && ex.Detalles is Dictionary<string, object> detalles
            && detalles.TryGetValue("fieldErrors", out var campos) && campos is Dictionary<string, string[]> porCampo)
        {
            foreach (var (campo, mensajes) in deCatalogo)
            {
                porCampo.TryAdd(campo, mensajes);
            }

            throw;
        }

        if (deCatalogo.Count > 0)
        {
            throw new clsValidacionException(
                "Datos inválidos",
                new Dictionary<string, object> { ["formErrors"] = Array.Empty<string>(), ["fieldErrors"] = deCatalogo });
        }
    }

    private async Task<FiltrosInspecciones> ParsearFiltrosAsync(FiltrosInspeccionesRequest s, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();

        string? vin = null;
        if (!string.IsNullOrWhiteSpace(s.Vin))
        {
            vin = clsVin.Normalizar(s.Vin);
            if (!clsVin.EsFormatoIso(vin))
            {
                errores["vin"] = ["VIN inválido: 17 caracteres alfanuméricos, sin las letras I, O ni Q"];
            }
        }

        Guid? usuarioId = null;
        if (!string.IsNullOrWhiteSpace(s.UsuarioId))
        {
            if (Guid.TryParse(s.UsuarioId, out var id))
            {
                usuarioId = id;
            }
            else
            {
                errores["usuarioId"] = ["usuarioId debe ser UUID"];
            }
        }

        var momento = Catalogo(s.Momento, clsMomento.VALORES, "momento", errores);
        var estado = Catalogo(s.Estado, clsEstadoInspeccion.VALORES, "estado", errores);
        // El proceso es una etapa del catalogo; se aceptan tambien las inactivas porque hay informes historicos de ellas.
        var proceso = Catalogo(s.Proceso, await catalogos.CodigosAsync(clsNombresCatalogo.ETAPAS, soloActivos: false, ct), "proceso", errores);
        var desde = Fecha(s.Desde, "desde", errores);
        var hasta = Fecha(s.Hasta, "hasta", errores);

        if (errores.Count > 0)
        {
            throw new clsValidacionException(
                "Datos inválidos",
                new Dictionary<string, object> { ["formErrors"] = Array.Empty<string>(), ["fieldErrors"] = errores });
        }

        return new FiltrosInspecciones(vin, usuarioId, momento, estado, proceso, desde, hasta);
    }

    private static string? Catalogo(string? valor, IReadOnlySet<string> catalogo, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        if (catalogo.Contains(valor))
        {
            return valor;
        }

        errores[campo] = [$"Valor inválido. Permitidos: {string.Join(", ", catalogo.Order())}"];
        return null;
    }

    /// <summary>Como el esquema original: fecha ISO 8601 con zona horaria explicita (Z o desfase).</summary>
    private static DateTimeOffset? Fecha(string? texto, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var t = texto.Trim();
        var conZona = t.EndsWith('Z') || t.EndsWith('z') || (t.Length > 6 && (t[^6] is '+' or '-') && t[^3] == ':');
        if (conZona && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            return fecha;
        }

        errores[campo] = [$"{campo} debe ser fecha ISO"];
        return null;
    }
}
