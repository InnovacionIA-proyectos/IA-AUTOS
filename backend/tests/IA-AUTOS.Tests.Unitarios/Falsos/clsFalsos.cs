using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Tests.Unitarios.Falsos;

/// <summary>Almacen de imagenes en memoria: lo guardado se lee por nombre.</summary>
public sealed class clsAlmacenArchivosFalso : IAlmacenArchivos
{
    public Dictionary<string, byte[]> Archivos { get; } = [];

    public Task GuardarAsync(string nombreArchivo, byte[] contenido, string tipoContenido, CancellationToken ct = default)
    {
        Archivos[nombreArchivo] = contenido;
        return Task.CompletedTask;
    }

    public Task<byte[]?> LeerAsync(string nombreArchivo, CancellationToken ct = default) =>
        Task.FromResult(Archivos.TryGetValue(nombreArchivo, out var contenido) ? contenido : null);
}

/// <summary>Imagen de los paneles de danos en memoria: un panel por codigo, con su archivo (o sin imagen).</summary>
public sealed class clsImagenesPanelFalso : IImagenesPanelDano
{
    public Dictionary<string, string?> Paneles { get; } = [];

    public Task<string?> ObtenerNombreAsync(string codigoPanel, CancellationToken ct = default) =>
        Paneles.TryGetValue(codigoPanel, out var nombre) ? Task.FromResult(nombre) : throw new clsEntidadNoEncontradaException($"No existe el panel {codigoPanel}");

    public Task CambiarAsync(string codigoPanel, string archivo, CancellationToken ct = default)
    {
        if (!Paneles.ContainsKey(codigoPanel))
        {
            throw new clsEntidadNoEncontradaException($"No existe el panel {codigoPanel}");
        }

        Paneles[codigoPanel] = archivo;
        return Task.CompletedTask;
    }
}

public sealed class clsRelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset Ahora { get; set; } = ahora;
}

public sealed class clsUsuarioActualFalso : IUsuarioActual
{
    public Guid? Id { get; set; }

    public string? Rol { get; set; }

    public string? Email { get; set; }

    public Guid? ClienteId { get; set; }
}

/// <summary>Clientes en memoria: dos clientes activos (sofasa primero) y la lectura minima del catalogo que usan los servicios de usuarios.</summary>
public sealed class clsCatalogosLecturaFalso : ICatalogosLectura
{
    public static readonly Guid SOFASA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid OTRO = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    public List<ValorCatalogo> Clientes { get; } =
    [
        new() { ["id"] = SOFASA, ["codigo"] = "sofasa", ["nombre"] = "Sofasa", ["orden"] = 1, ["activo"] = true },
        new() { ["id"] = OTRO, ["codigo"] = "otro", ["nombre"] = "Otro cliente", ["orden"] = 2, ["activo"] = true },
    ];

    public Task<IReadOnlySet<string>> CodigosAsync(string catalogo, bool soloActivos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    public Task<IReadOnlyList<clsEtapaDeRuta>> RutaEstandarAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<clsEtapaDeRuta>>([]);

    public Task<IReadOnlyList<ValorCatalogo>> ValoresAsync(string catalogo, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ValorCatalogo>>(catalogo == clsNombresCatalogo.CLIENTES ? Clientes : []);

    public Task<bool> ExisteActivoAsync(string catalogo, Guid id, CancellationToken ct = default) =>
        Task.FromResult(catalogo == clsNombresCatalogo.CLIENTES && Clientes.Any(c => c.Id == id && c.Activo));

    public Task<Guid> ClienteAsync(string? codigoPreferido, CancellationToken ct = default)
    {
        var activos = Clientes.Where(c => c.Activo).ToList();
        return Task.FromResult((activos.FirstOrDefault(c => c.Codigo == codigoPreferido) ?? activos.First()).Id);
    }
}

public sealed class clsEmisorTokenFalso : IEmisorToken
{
    public string Emitir(UsuarioAutenticadoDto usuario) => $"token-de-{usuario.Id}";

    public string EmitirSesion(UsuarioAutenticadoDto usuario, Guid familiaId, TimeSpan vigencia) => $"sesion-de-{usuario.Id}-{familiaId}";
}

/// <summary>
/// Unidad de trabajo en memoria. Los repositorios falsos aplican sus cambios al confirmar y los revierten al
/// descartar, igual que el DbContext real. Permite simular un fallo en la proxima confirmacion.
/// </summary>
public sealed class clsUnidadDeTrabajoFalsa : IUnidadDeTrabajo
{
    private readonly Queue<Exception> _fallos = new();

    public int Confirmaciones { get; private set; }

    public int Descartes { get; private set; }

    public event Action? Confirmado;

    public event Action? Descartado;

    public void FallarEnLaProximaConfirmacion(Exception ex) => _fallos.Enqueue(ex);

    public Task<int> GuardarCambiosAsync(CancellationToken ct = default)
    {
        if (_fallos.TryDequeue(out var ex))
        {
            throw ex;
        }

        Confirmaciones++;
        Confirmado?.Invoke();
        return Task.FromResult(1);
    }

    public void DescartarCambios()
    {
        Descartes++;
        Descartado?.Invoke();
    }
}

public sealed class clsAuditoriaRepositoryFalso : IAuditoriaRepository
{
    private readonly List<clsAuditLog> _pendientes = [];

    public clsAuditoriaRepositoryFalso(clsUnidadDeTrabajoFalsa unidadDeTrabajo)
    {
        unidadDeTrabajo.Confirmado += () =>
        {
            Registros.AddRange(_pendientes);
            _pendientes.Clear();
        };
        unidadDeTrabajo.Descartado += _pendientes.Clear;
    }

    /// <summary>Registros ya confirmados.</summary>
    public List<clsAuditLog> Registros { get; } = [];

    public void Agregar(clsAuditLog registro) => _pendientes.Add(registro);
}

public sealed class clsUsuarioRepositoryFalso : IUsuarioRepository
{
    private readonly List<clsUsuario> _pendientesDeEliminar = [];

    public clsUsuarioRepositoryFalso(clsUnidadDeTrabajoFalsa unidadDeTrabajo)
    {
        unidadDeTrabajo.Confirmado += () =>
        {
            foreach (var u in _pendientesDeEliminar)
            {
                Usuarios.Remove(u);
            }

            _pendientesDeEliminar.Clear();
        };
        unidadDeTrabajo.Descartado += _pendientesDeEliminar.Clear;
    }

    public List<clsUsuario> Usuarios { get; } = [];

    public HashSet<Guid> ConHistorial { get; } = [];

    public Task<clsUsuario?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Usuarios.FirstOrDefault(u => u.Id == id));

    public Task<clsUsuario?> ObtenerPorEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Usuarios.FirstOrDefault(u => string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task<clsUsuario?> ObtenerPorPortalIdAsync(long portalUsuarioId, CancellationToken ct = default) =>
        Task.FromResult(Usuarios.FirstOrDefault(u => u.PortalUsuarioId == portalUsuarioId));

    public Task<IReadOnlyDictionary<string, clsUsuario>> ObtenerPorCedulasAsync(IReadOnlyCollection<string> cedulas, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, clsUsuario>>(Usuarios.Where(u => u.Cedula is not null && cedulas.Contains(u.Cedula)).ToDictionary(u => u.Cedula!));

    public Task<clsUsuario?> ObtenerPorCedulaAsync(string cedula, CancellationToken ct = default) =>
        Task.FromResult(Usuarios.FirstOrDefault(u => u.Cedula == cedula));

    public Task<IReadOnlyList<clsUsuario>> ObtenerActivosPorRolesAsync(IReadOnlyCollection<string> roles, Guid clienteId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<clsUsuario>>(Usuarios.Where(u => u.Activo && u.ClienteId == clienteId && roles.Contains(u.Rol)).OrderBy(u => u.Nombre).ToList());

    public Task<IReadOnlyList<clsUsuario>> ListarAsync(Guid? clienteId = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<clsUsuario>>(Usuarios.Where(u => clienteId == null || u.ClienteId == clienteId).OrderBy(u => u.Nombre).ToList());

    public Task<(IReadOnlyList<clsUsuario> Filas, int Total)> BuscarAsync(clsFiltroUsuarios filtro, int saltar, int tomar, CancellationToken ct = default)
    {
        var consulta = Usuarios.Where(u =>
            (filtro.ClienteId == null || u.ClienteId == filtro.ClienteId)
            && (filtro.Rol == null || u.Rol == filtro.Rol)
            && (filtro.Activo == null || u.Activo == filtro.Activo)
            && (string.IsNullOrWhiteSpace(filtro.Texto)
                || u.Nombre.Contains(filtro.Texto, StringComparison.OrdinalIgnoreCase)
                || (u.Email?.Contains(filtro.Texto, StringComparison.OrdinalIgnoreCase) ?? false)
                || (u.Cedula?.Contains(filtro.Texto, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
        return Task.FromResult<(IReadOnlyList<clsUsuario>, int)>((consulta.OrderBy(u => u.Nombre).Skip(saltar).Take(tomar).ToList(), consulta.Count));
    }

    public Task<IReadOnlySet<string>> EmailsExistentesAsync(IReadOnlyCollection<string> emails, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(Usuarios.Where(u => u.Email is not null && emails.Contains(u.Email)).Select(u => u.Email!).ToHashSet());

    public Task<IReadOnlySet<string>> CedulasExistentesAsync(IReadOnlyCollection<string> cedulas, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(Usuarios.Where(u => u.Cedula is not null && cedulas.Contains(u.Cedula)).Select(u => u.Cedula!).ToHashSet());

    public Task<bool> TieneHistorialAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(ConHistorial.Contains(id));

    public void Agregar(clsUsuario usuario) => Usuarios.Add(usuario);

    public void Eliminar(clsUsuario usuario) => _pendientesDeEliminar.Add(usuario);
}

/// <summary>Refresh tokens en memoria (los cambios se aplican al instante, como las revocaciones directas del repositorio real).</summary>
public sealed class clsRefreshTokenRepositoryFalso : IRefreshTokenRepository
{
    public List<clsRefreshToken> Tokens { get; } = [];

    public Task<clsRefreshToken?> ObtenerPorHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

    public void Agregar(clsRefreshToken token) => Tokens.Add(token);

    public Task<bool> FamiliaActivaAsync(Guid familiaId, DateTimeOffset ahora, CancellationToken ct = default) =>
        Task.FromResult(Tokens.Any(t => t.FamiliaId == familiaId && t.EstaVigente(ahora)));

    public Task RevocarFamiliaAsync(Guid familiaId, DateTimeOffset ahora, CancellationToken ct = default)
    {
        foreach (var token in Tokens.Where(t => t.FamiliaId == familiaId))
        {
            token.Revocar(ahora);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Base de usuarios del portal (TI) simulada.</summary>
public sealed class clsPortalUsuariosFalso : IPortalUsuarios
{
    public bool Disponible { get; set; } = true;

    /// <summary>Si es true, TI "no responde" y la consulta lanza el error de servicio no disponible.</summary>
    public bool Falla { get; set; }

    public Dictionary<long, clsUsuarioPortal> Usuarios { get; } = [];

    public int Consultas { get; private set; }

    public Task<clsUsuarioPortal?> ObtenerAsync(long id, CancellationToken ct = default)
    {
        Consultas++;
        if (Falla)
        {
            throw new clsServicioNoDisponibleException("TI no responde");
        }

        return Task.FromResult(Usuarios.GetValueOrDefault(id));
    }
}
