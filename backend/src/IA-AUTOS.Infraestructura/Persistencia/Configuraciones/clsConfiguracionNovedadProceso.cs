using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionNovedadProceso : IEntityTypeConfiguration<clsNovedadProceso>
{
    public void Configure(EntityTypeBuilder<clsNovedadProceso> builder)
    {
        // La novedad no se modifica ni se borra y su texto lo valida un trigger (INSTEAD OF y AFTER): EF no puede leer valores generados con OUTPUT.
        builder.ToTable("tblNovedadesProceso", t =>
        {
            t.HasTrigger("TR_tblNovedadesProceso_NoModificar");
            t.HasTrigger("TR_tblNovedadesProceso_NoBorrar");
            t.HasTrigger("TR_tblNovedadesProceso_TextoDelMotivo");
        });
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Tipo).HasColumnType("varchar(30)").IsRequired();
        builder.Property(n => n.MotivoCodigo).HasColumnType("varchar(30)");
        builder.Property(n => n.MotivoTexto).HasMaxLength(clsNovedadProceso.TEXTO_MAX);
        builder.Property(n => n.Fecha).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(n => n.CreatedAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(n => n.ViajeId)
            .HasConstraintName("FK_tblNovedadesProceso_Viaje").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(n => n.VehiculoId)
            .HasConstraintName("FK_tblNovedadesProceso_Vehiculo").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsEtapa>().WithMany().HasForeignKey(n => n.EtapaId)
            .HasConstraintName("FK_tblNovedadesProceso_Etapa").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsInspeccion>().WithMany().HasForeignKey(n => n.InspeccionId)
            .HasConstraintName("FK_tblNovedadesProceso_Inspeccion").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(n => n.UsuarioId)
            .HasConstraintName("FK_tblNovedadesProceso_Usuario").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(n => new { n.InspeccionId, n.Tipo }).IsUnique().HasDatabaseName("UX_tblNovedadesProceso_Inspeccion_Tipo");
        builder.HasIndex(n => n.ViajeId).HasDatabaseName("IX_tblNovedadesProceso_Viaje");
        builder.HasIndex(n => n.VehiculoId).HasDatabaseName("IX_tblNovedadesProceso_Vehiculo");
    }
}
