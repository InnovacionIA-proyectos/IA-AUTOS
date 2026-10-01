using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Tests.Unitarios.Falsos;

public sealed class clsRelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset Ahora { get; set; } = ahora;
}

public sealed class clsUsuarioActualFalso : IUsuarioActual
{
    public Guid? Id { get; set; }

    public string? Rol { get; set; }

    public string? Email { get; set; }
}

public sealed class clsEmisorTokenFalso : IEmisorToken
{
    public string Emitir(UsuarioAutenticadoDto usuario) => $"token-de-{usuario.Id}";
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

    public Task<IReadOnlyList<clsUsuario>> ListarAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<clsUsuario>>(Usuarios.OrderBy(u => u.Nombre).ToList());

    public Task<bool> TieneHistorialAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(ConHistorial.Contains(id));

    public void Agregar(clsUsuario usuario) => Usuarios.Add(usuario);

    public void Eliminar(clsUsuario usuario) => _pendientesDeEliminar.Add(usuario);
}
