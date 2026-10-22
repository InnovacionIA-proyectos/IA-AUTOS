using FluentValidation;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Usuarios;

public sealed class CargarConductoresRequestValidator : AbstractValidator<CargarConductoresRequest>
{
    public const int MAXIMO_FILAS = 500;

    public CargarConductoresRequestValidator()
    {
        RuleFor(x => x.Filas)
            .Must(f => f is { Count: > 0 }).WithMessage("Se requiere al menos una fila")
            .Must(f => f is null || f.Count <= MAXIMO_FILAS).WithMessage($"Máximo {MAXIMO_FILAS} filas por carga");
        RuleForEach(x => x.Filas).Must(f => f is not null).WithMessage("Fila inválida");
    }
}

/// <summary>
/// Carga masiva de conductores desde un Excel (una fila por conductor: nombre, cedula y correo opcional). Primero se muestra una vista
/// previa con el error de cada fila (sin guardar nada) y al confirmar se crea un conductor por cada fila valida, con un PIN temporal
/// generado que se devuelve una sola vez (la persona lo cambia en su primer ingreso). Una fila invalida no frena a las demas.
/// </summary>
public sealed class clsCargaConductoresService(
    IUsuarioRepository usuarios,
    IServicioContrasenas contrasenas,
    ICatalogosLectura catalogos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<CargarConductoresRequest> validador,
    ILogger<clsCargaConductoresService> registro)
{
    private sealed class Fila
    {
        public int Numero { get; init; }

        public string? Nombre { get; init; }

        public string? Cedula { get; init; }

        public string? Email { get; init; }

        public List<string> Errores { get; } = [];

        public bool Valida => Errores.Count == 0;

        public Guid? UsuarioId { get; set; }

        public string? Pin { get; set; }
    }

    public Task<ResultadoCargaConductoresDto> PrevisualizarAsync(CargarConductoresRequest solicitud, CancellationToken ct = default) =>
        ProcesarAsync(solicitud, confirmar: false, ct);

    public Task<ResultadoCargaConductoresDto> CargarAsync(CargarConductoresRequest solicitud, CancellationToken ct = default) =>
        ProcesarAsync(solicitud, confirmar: true, ct);

    private async Task<ResultadoCargaConductoresDto> ProcesarAsync(CargarConductoresRequest solicitud, bool confirmar, CancellationToken ct)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var filas = Limpiar(solicitud.Filas!);
        await ValidarAsync(filas, ct);

        var validas = filas.Where(f => f.Valida).ToList();
        var creados = validas.Count;
        if (confirmar)
        {
            creados = await GuardarAsync(validas, ct);
            auditor.Registrar(
                "usuarios.cargar_conductores", "usuarios", detalle: new DetalleAuditoria(null, new { filas = filas.Count, creados }));
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }

        var resultado = filas
            .Select(f => new FilaCargaConductorDto(f.Numero, f.Nombre, f.Cedula, f.Valida, f.Errores, f.UsuarioId, f.Pin))
            .ToList();
        var buenas = resultado.Count(f => f.Valida);
        return new ResultadoCargaConductoresDto(confirmar, resultado.Count, buenas, resultado.Count - buenas, creados, resultado);
    }

    private static List<Fila> Limpiar(IReadOnlyList<FilaCargaConductorRequest> entrada) =>
        entrada.Select((f, i) => new Fila
        {
            Numero = f.Fila ?? i + 1,
            Nombre = clsValidador.Opcional(f.Nombre),
            Cedula = CedulaOTexto(f.Cedula),
            Email = clsValidador.Opcional(f.Email)?.ToLowerInvariant(),
        }).ToList();

    /// <summary>La cedula se compara normalizada; si no es valida se deja el texto para que la validacion de la fila lo reporte.</summary>
    private static string? CedulaOTexto(string? cedula)
    {
        try
        {
            return clsUsuario.NormalizarCedula(cedula);
        }
        catch (clsValidacionException)
        {
            return clsValidador.Opcional(cedula);
        }
    }

    private async Task ValidarAsync(List<Fila> filas, CancellationToken ct)
    {
        var cedulasEnBase = await usuarios.CedulasExistentesAsync(filas.Where(f => f.Cedula is not null).Select(f => f.Cedula!).ToList(), ct);
        var emailsEnBase = await usuarios.EmailsExistentesAsync(filas.Where(f => f.Email is not null).Select(f => f.Email!).ToList(), ct);
        var cedulasVistas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var emailsVistos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var fila in filas)
        {
            if (fila.Nombre is null)
            {
                fila.Errores.Add("El nombre es requerido");
            }
            else if (fila.Nombre.Length > 200)
            {
                fila.Errores.Add("El nombre admite máximo 200 caracteres");
            }

            ValidarCedula(fila, cedulasEnBase, cedulasVistas);
            ValidarEmail(fila, emailsEnBase, emailsVistos);
        }
    }

    private static void ValidarCedula(Fila fila, IReadOnlySet<string> enBase, Dictionary<string, int> vistas)
    {
        if (fila.Cedula is null)
        {
            fila.Errores.Add("La cédula es requerida");
            return;
        }

        try
        {
            clsUsuario.NormalizarCedula(fila.Cedula);
        }
        catch (clsValidacionException)
        {
            fila.Errores.Add($"La cédula admite letras, números y guion (de {clsUsuario.CEDULA_MIN} a {clsUsuario.CEDULA_MAX} caracteres)");
            return;
        }

        if (enBase.Contains(fila.Cedula))
        {
            fila.Errores.Add($"Ya existe un usuario con la cédula {fila.Cedula}");
        }
        else if (vistas.TryGetValue(fila.Cedula, out var primera))
        {
            fila.Errores.Add($"La cédula {fila.Cedula} está repetida en el archivo (fila {primera})");
        }
        else
        {
            vistas[fila.Cedula] = fila.Numero;
        }
    }

    private static void ValidarEmail(Fila fila, IReadOnlySet<string> enBase, Dictionary<string, int> vistos)
    {
        if (fila.Email is null)
        {
            return;
        }

        try
        {
            clsUsuario.NormalizarEmail(fila.Email);
        }
        catch (clsValidacionException)
        {
            fila.Errores.Add("El correo no es válido");
            return;
        }

        if (enBase.Contains(fila.Email))
        {
            fila.Errores.Add($"Ya existe un usuario con el correo {fila.Email}");
        }
        else if (vistos.TryGetValue(fila.Email, out var primera))
        {
            fila.Errores.Add($"El correo {fila.Email} está repetido en el archivo (fila {primera})");
        }
        else
        {
            vistos[fila.Email] = fila.Numero;
        }
    }

    /// <summary>
    /// Crea un conductor por fila valida (cada uno se confirma por separado: si uno falla, por ejemplo por una cedula repetida justo
    /// en ese momento, su fila queda con el error y los demas siguen). Los hashes de los PIN se calculan en paralelo porque son costosos.
    /// </summary>
    private async Task<int> GuardarAsync(List<Fila> validas, CancellationToken ct)
    {
        var clienteId = actual.Rol is null or clsRol.SUPER_ADMIN
            ? await catalogos.ClienteAsync(null, ct)
            : actual.ClienteId ?? await catalogos.ClienteAsync(null, ct);

        var pines = validas.Select(_ => clsPin.Generar()).ToArray();
        var hashes = new string[validas.Count];
        Parallel.For(0, validas.Count, new ParallelOptions { CancellationToken = ct }, i => hashes[i] = contrasenas.Hashear(pines[i]));

        var creados = 0;
        for (var i = 0; i < validas.Count; i++)
        {
            var fila = validas[i];
            try
            {
                var usuario = clsUsuario.CrearConPin(fila.Nombre!, fila.Email, clsRol.CONDUCTOR, null, fila.Cedula, hashes[i], reloj.Ahora, clienteId);
                usuarios.Agregar(usuario);
                auditor.Registrar("usuario.crear", "usuarios", usuario.Id, detalle: new DetalleAuditoria(null, clsInstantaneasAuditoria.De(usuario)), clienteId: usuario.ClienteId);
                await unidadDeTrabajo.GuardarCambiosAsync(ct);
                fila.UsuarioId = usuario.Id;
                fila.Pin = pines[i];
                creados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unidadDeTrabajo.DescartarCambios();
                if (ex is not clsExcepcionDominio)
                {
                    registro.LogWarning(ex, "No se pudo crear el conductor de la fila {Fila} en la carga masiva", fila.Numero);
                }

                fila.Errores.Add(ex is clsExcepcionDominio dominio ? dominio.Message : "No se pudo crear el conductor");
            }
        }

        return creados;
    }
}
