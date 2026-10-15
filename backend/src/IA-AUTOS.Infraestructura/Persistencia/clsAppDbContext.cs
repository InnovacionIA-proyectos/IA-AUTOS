using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using IAAutos.Infraestructura.Persistencia.Consultas;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia;

/// <summary>
/// Contexto de EF Core sobre SQL Server 2019. El esquema lo crea database/ddl.sql (no se usan migraciones de EF):
/// las configuraciones solo describen el mapeo hacia las tablas tbl* ya existentes.
/// </summary>
public sealed class clsAppDbContext : DbContext
{
    public clsAppDbContext(DbContextOptions<clsAppDbContext> opciones)
        : base(opciones)
    {
    }

    public DbSet<clsUsuario> Usuarios => Set<clsUsuario>();
    public DbSet<clsRefreshToken> RefreshTokens => Set<clsRefreshToken>();

    public DbSet<clsVehiculo> Vehiculos => Set<clsVehiculo>();

    public DbSet<clsEtapaCatalogo> EtapasCatalogo => Set<clsEtapaCatalogo>();
    public DbSet<clsPuntoCustodia> PuntosCustodia => Set<clsPuntoCustodia>();
    public DbSet<clsTipoUnidadTransportadora> TiposUnidad => Set<clsTipoUnidadTransportadora>();
    public DbSet<clsClaseDano> ClasesDano => Set<clsClaseDano>();
    public DbSet<clsTamanoDano> TamanosDano => Set<clsTamanoDano>();
    public DbSet<clsNivelSeveridad> NivelesSeveridad => Set<clsNivelSeveridad>();
    public DbSet<clsVistaFoto> VistasFoto => Set<clsVistaFoto>();
    public DbSet<clsCuadrante> Cuadrantes => Set<clsCuadrante>();
    public DbSet<clsCuadranteVista> CuadranteVistas => Set<clsCuadranteVista>();

    public DbSet<clsEtapa> Etapas => Set<clsEtapa>();

    public DbSet<clsInspeccion> Inspecciones => Set<clsInspeccion>();

    public DbSet<clsHallazgo> Hallazgos => Set<clsHallazgo>();

    public DbSet<clsEvidencia> Evidencias => Set<clsEvidencia>();

    public DbSet<clsTransferenciaCustodia> Transferencias => Set<clsTransferenciaCustodia>();

    public DbSet<clsViaje> Viajes => Set<clsViaje>();

    public DbSet<clsViajeVehiculo> ViajeVehiculos => Set<clsViajeVehiculo>();

    public DbSet<clsOrdenSofasa> OrdenesSofasa => Set<clsOrdenSofasa>();

    public DbSet<clsAuditLog> AuditLog => Set<clsAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(clsAppDbContext).Assembly);
        modelBuilder.Entity<clsViajeCompleto>().HasNoKey().ToView("vwViajesCompletos");
    }
}
