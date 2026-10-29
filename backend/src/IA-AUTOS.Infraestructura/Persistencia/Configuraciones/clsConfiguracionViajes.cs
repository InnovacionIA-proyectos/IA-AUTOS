using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Inspecciones;
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
        builder.Property(v => v.FechaProgramada).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsLugar>().WithMany().HasForeignKey(v => v.LugarCargueId)
            .HasConstraintName("FK_tblViajes_LugarCargue").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(v => v.LugarCargueId).HasDatabaseName("IX_tblViajes_LugarCargue");
        builder.HasIndex(v => v.FechaProgramada).HasDatabaseName("IX_tblViajes_FechaProgramada");

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
        builder.ToTable("tblViajeVehiculos", t => t.HasTrigger("TR_tblViajeVehiculos_Completa"));
        clsConfiguracionBase.Base(builder);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(v => v.ViajeId)
            .HasConstraintName("FK_tblViajeVehiculos_Viaje").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(v => v.VehiculoId)
            .HasConstraintName("FK_tblViajeVehiculos_Vehiculo").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsLugar>().WithMany().HasForeignKey(v => v.LugarDestinoId)
            .HasConstraintName("FK_tblViajeVehiculos_LugarDestino").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(v => v.LugarDestinoId).HasDatabaseName("IX_tblViajeVehiculos_LugarDestino");
        builder.Property(v => v.EstadoUnidad).HasMaxLength(10).HasDefaultValue(clsEstadoUnidad.ACTIVA);
        // Completa la calculan los triggers de la base: la aplicacion nunca la escribe.
        builder.Property(v => v.Completa).HasDefaultValue(false)
            .Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        builder.Property(v => v.Completa).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        builder.Property(v => v.NoSaleMotivo).HasMaxLength(clsViajeVehiculo.NO_SALE_MOTIVO_MAX);
        builder.HasOne<clsHallazgo>().WithMany().HasForeignKey(v => v.NoSaleHallazgoId)
            .HasConstraintName("FK_tblViajeVehiculos_NoSaleHallazgo").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(v => v.NoSaleMarcadoPor)
            .HasConstraintName("FK_tblViajeVehiculos_NoSaleMarcadoPor").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(v => v.ReemplazoVehiculoId)
            .HasConstraintName("FK_tblViajeVehiculos_Reemplazo").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(v => new { v.ViajeId, v.VehiculoId }).IsUnique().HasDatabaseName("UQ_tblViajeVehiculos_Viaje_Vehiculo");
        builder.HasIndex(v => v.VehiculoId).HasDatabaseName("IX_tblViajeVehiculos_Vehiculo");
    }
}
