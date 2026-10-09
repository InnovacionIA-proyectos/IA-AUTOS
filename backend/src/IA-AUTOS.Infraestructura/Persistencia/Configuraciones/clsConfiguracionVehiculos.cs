using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionVehiculo : IEntityTypeConfiguration<clsVehiculo>
{
    public void Configure(EntityTypeBuilder<clsVehiculo> builder)
    {
        builder.ToTable("tblVehiculos");
        clsConfiguracionBase.Base(builder);
        builder.Property(v => v.Vin).HasColumnType("varchar(64)").IsRequired();
        builder.Property(v => v.Marca).HasMaxLength(100).IsRequired();
        builder.Property(v => v.TipoVehiculo).HasMaxLength(100);
        builder.Property(v => v.Modelo).HasMaxLength(300);
        builder.Property(v => v.Chasis).HasMaxLength(100);
        builder.Property(v => v.Color).HasMaxLength(100);
        builder.Property(v => v.Operacion).HasColumnType("varchar(20)").IsRequired();
        builder.HasIndex(v => v.Vin).IsUnique().HasDatabaseName("UQ_tblVehiculos_Vin");
    }
}

internal sealed class clsConfiguracionPuntoCustodia : IEntityTypeConfiguration<clsPuntoCustodia>
{
    public void Configure(EntityTypeBuilder<clsPuntoCustodia> builder)
    {
        builder.ToTable("tblPuntosCustodia");
        clsConfiguracionBase.Base(builder);
        builder.Property(p => p.Nombre).HasColumnType("varchar(30)").IsRequired();
        builder.HasIndex(p => p.Nombre).IsUnique().HasDatabaseName("UQ_tblPuntosCustodia_Nombre");
    }
}

internal sealed class clsConfiguracionEtapa : IEntityTypeConfiguration<clsEtapa>
{
    public void Configure(EntityTypeBuilder<clsEtapa> builder)
    {
        builder.ToTable("tblEtapas");
        clsConfiguracionBase.Base(builder);
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(e => e.VehiculoId)
            .HasConstraintName("FK_tblEtapas_Vehiculo").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<clsPuntoCustodia>().WithMany().HasForeignKey(e => e.PuntoCustodiaId)
            .HasConstraintName("FK_tblEtapas_PuntoCustodia").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => new { e.VehiculoId, e.Orden }).IsUnique().HasDatabaseName("UQ_tblEtapas_Vehiculo_Orden");
    }
}

internal sealed class clsConfiguracionUsuario : IEntityTypeConfiguration<clsUsuario>
{
    public void Configure(EntityTypeBuilder<clsUsuario> builder)
    {
        builder.ToTable("tblUsuarios");
        clsConfiguracionBase.Base(builder);
        builder.Property(u => u.Nombre).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.Rol).HasColumnType("varchar(30)").IsRequired();
        builder.Property(u => u.SubtipoInspector).HasColumnType("varchar(30)");
        builder.Property(u => u.PasswordHash).HasMaxLength(400);
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("UQ_tblUsuarios_Email");

        // Id del portal: unico entre quienes lo tienen (los conductores no tienen usuario del portal y quedan en NULL).
        builder.Property(u => u.PortalUsuarioId).HasColumnType("bigint");
        builder.HasIndex(u => u.PortalUsuarioId).IsUnique().HasFilter("[PortalUsuarioId] IS NOT NULL").HasDatabaseName("UX_tblUsuarios_PortalUsuarioId");
    }
}
