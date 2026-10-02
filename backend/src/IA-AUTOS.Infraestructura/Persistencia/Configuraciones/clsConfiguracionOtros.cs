using IAAutos.Dominio.Asignaciones;
using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionAsignacion : IEntityTypeConfiguration<clsAsignacion>
{
    public void Configure(EntityTypeBuilder<clsAsignacion> builder)
    {
        builder.ToTable("tblAsignaciones");
        clsConfiguracionBase.Base(builder);
        builder.Property(a => a.Ninera).HasMaxLength(100).IsRequired();
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(a => a.VehiculoId)
            .HasConstraintName("FK_tblAsignaciones_Vehiculo").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(a => a.ConductorId)
            .HasConstraintName("FK_tblAsignaciones_Conductor").OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(a => a.AsignadoPor)
            .HasConstraintName("FK_tblAsignaciones_AsignadoPor").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class clsConfiguracionOrdenSofasa : IEntityTypeConfiguration<clsOrdenSofasa>
{
    public void Configure(EntityTypeBuilder<clsOrdenSofasa> builder)
    {
        builder.ToTable("tblOrdenesSofasa");
        clsConfiguracionBase.Base(builder);
        builder.Property(o => o.StatusCode).HasMaxLength(100);
        builder.Property(o => o.DatetimeEvent).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(o => o.OrdenNumber).HasMaxLength(255);
        builder.Property(o => o.OrdenSap).HasMaxLength(255);
        builder.Property(o => o.OriginCedi).HasMaxLength(255);
        builder.Property(o => o.Vin).HasColumnType("varchar(64)");
        builder.Property(o => o.Make).HasMaxLength(255);
        builder.Property(o => o.ModelDescription).HasMaxLength(500);
        builder.Property(o => o.DealerCode).HasMaxLength(255);
        builder.Property(o => o.DestinationCity).HasMaxLength(255);
        builder.Property(o => o.DestinationAddress).HasMaxLength(1000);
        builder.Property(o => o.LoadingDate).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(o => o.Appointment).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(o => o.LoadingDock).HasMaxLength(255);
        builder.Property(o => o.Weight).HasColumnType("decimal(18,4)");
        builder.Property(o => o.SyncedAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(o => o.VehiculoId)
            .HasConstraintName("FK_tblOrdenesSofasa_Vehiculo").OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(o => o.SourceId).IsUnique().HasDatabaseName("UQ_tblOrdenesSofasa_SourceId");
    }
}

internal sealed class clsConfiguracionAuditLog : IEntityTypeConfiguration<clsAuditLog>
{
    public void Configure(EntityTypeBuilder<clsAuditLog> builder)
    {
        builder.ToTable("tblAuditLog");
        clsConfiguracionBase.Base(builder);
        builder.Property(a => a.Accion).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Entidad).HasMaxLength(100);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(a => a.UsuarioId)
            .HasConstraintName("FK_tblAuditLog_Usuario").OnDelete(DeleteBehavior.NoAction);
    }
}
