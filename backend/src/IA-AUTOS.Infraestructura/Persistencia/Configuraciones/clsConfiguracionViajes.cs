using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionViaje : IEntityTypeConfiguration<clsViaje>
{
    public void Configure(EntityTypeBuilder<clsViaje> builder)
    {
        builder.ToTable("tblViajes");
        clsConfiguracionBase.Base(builder);
        builder.Property(v => v.CodigoMtrack).HasColumnType("varchar(50)");
        builder.Property(v => v.Placa).HasMaxLength(clsViaje.PLACA_MAX);
        builder.Property(v => v.Trailer).HasMaxLength(clsViaje.TRAILER_MAX);
        builder.Property(v => v.Estado).HasColumnType("varchar(20)").IsRequired();
        builder.HasOne<clsTipoUnidadTransportadora>().WithMany().HasForeignKey(v => v.TipoUnidadId)
            .HasConstraintName("FK_tblViajes_TipoUnidad").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(v => v.ConductorId)
            .HasConstraintName("FK_tblViajes_Conductor").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(v => v.InspectorId)
            .HasConstraintName("FK_tblViajes_Inspector").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(v => v.CreadoPor)
            .HasConstraintName("FK_tblViajes_CreadoPor").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsPuntoCustodia>().WithMany().HasForeignKey(v => v.OrigenId)
            .HasConstraintName("FK_tblViajes_Origen").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsPuntoCustodia>().WithMany().HasForeignKey(v => v.DestinoId)
            .HasConstraintName("FK_tblViajes_Destino").OnDelete(DeleteBehavior.NoAction);

        // El codigo MTrack es unico entre los viajes que lo tienen (los migrados pueden no tenerlo).
        builder.HasIndex(v => v.CodigoMtrack).IsUnique().HasFilter("[CodigoMtrack] IS NOT NULL").HasDatabaseName("UX_tblViajes_CodigoMtrack");
        builder.HasIndex(v => v.ConductorId).HasDatabaseName("IX_tblViajes_Conductor");
        builder.HasIndex(v => v.InspectorId).HasDatabaseName("IX_tblViajes_Inspector");
    }
}

internal sealed class clsConfiguracionViajeVehiculo : IEntityTypeConfiguration<clsViajeVehiculo>
{
    public void Configure(EntityTypeBuilder<clsViajeVehiculo> builder)
    {
        builder.ToTable("tblViajeVehiculos");
        clsConfiguracionBase.Base(builder);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(v => v.ViajeId)
            .HasConstraintName("FK_tblViajeVehiculos_Viaje").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(v => v.VehiculoId)
            .HasConstraintName("FK_tblViajeVehiculos_Vehiculo").OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => new { v.ViajeId, v.VehiculoId }).IsUnique().HasDatabaseName("UQ_tblViajeVehiculos_Viaje_Vehiculo");
        builder.HasIndex(v => v.VehiculoId).HasDatabaseName("IX_tblViajeVehiculos_Vehiculo");
    }
}
