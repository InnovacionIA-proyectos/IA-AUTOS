using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionPermisoSinInspector : IEntityTypeConfiguration<clsPermisoSinInspector>
{
    public void Configure(EntityTypeBuilder<clsPermisoSinInspector> builder)
    {
        builder.ToTable("tblPermisosSinInspector");
        clsConfiguracionBase.Base(builder);
        builder.Property(p => p.MotivoCodigo).HasColumnType("varchar(30)").IsRequired();
        builder.Property(p => p.MotivoTexto).HasMaxLength(clsPermisoSinInspector.TEXTO_MAX);
        builder.Property(p => p.Fecha).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(p => p.Estado).HasColumnType("varchar(10)").IsRequired();
        builder.Property(p => p.ConcedidoAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Ignore(p => p.EstaConcedido);
        builder.Ignore(p => p.VenceAt);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(p => p.ViajeId).HasConstraintName("FK_tblPermisosSinInspector_Viaje").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(p => p.ConductorId).HasConstraintName("FK_tblPermisosSinInspector_Conductor").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsLugar>().WithMany().HasForeignKey(p => p.LugarId).HasConstraintName("FK_tblPermisosSinInspector_Lugar").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(p => p.ConcedidoPor).HasConstraintName("FK_tblPermisosSinInspector_ConcedidoPor").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsMotivoSinInspector>().WithMany().HasForeignKey(p => p.MotivoCodigo).HasPrincipalKey(m => m.Codigo)
            .HasConstraintName("FK_tblPermisosSinInspector_Motivo").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(p => p.ViajeId).IsUnique().HasDatabaseName("UQ_tblPermisosSinInspector_Viaje");
        builder.HasIndex(p => new { p.Estado, p.CreatedAt }).HasDatabaseName("IX_tblPermisosSinInspector_Estado");
    }
}
