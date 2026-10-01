using IAAutos.Dominio.Asignaciones;
using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
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

    public DbSet<clsVehiculo> Vehiculos => Set<clsVehiculo>();

    public DbSet<clsPuntoCustodia> PuntosCustodia => Set<clsPuntoCustodia>();

    public DbSet<clsEtapa> Etapas => Set<clsEtapa>();

    public DbSet<clsInspeccion> Inspecciones => Set<clsInspeccion>();

    public DbSet<clsHallazgo> Hallazgos => Set<clsHallazgo>();

    public DbSet<clsEvidencia> Evidencias => Set<clsEvidencia>();

    public DbSet<clsTransferenciaCustodia> Transferencias => Set<clsTransferenciaCustodia>();

    public DbSet<clsAsignacion> Asignaciones => Set<clsAsignacion>();

    public DbSet<clsOrdenSofasa> OrdenesSofasa => Set<clsOrdenSofasa>();

    public DbSet<clsAuditLog> AuditLog => Set<clsAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(clsAppDbContext).Assembly);
        modelBuilder.Entity<clsVehiculoMovilizado>().HasNoKey().ToView("vwVehiculosMovilizados");
    }
}
