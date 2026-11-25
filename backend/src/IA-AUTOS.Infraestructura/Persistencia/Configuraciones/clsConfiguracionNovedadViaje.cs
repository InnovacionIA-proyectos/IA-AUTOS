using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionTipoNovedadViaje : IEntityTypeConfiguration<clsTipoNovedadViaje>
{
    public void Configure(EntityTypeBuilder<clsTipoNovedadViaje> builder)
    {
        clsConfiguracionCatalogo.Codificado(builder, "tblTiposNovedadViaje");
        builder.Property(t => t.Rol).HasColumnType("varchar(10)").IsRequired();
        builder.Property(t => t.Urgencia).HasColumnType("varchar(10)").IsRequired();
    }
}

internal sealed class clsConfiguracionNovedadViaje : IEntityTypeConfiguration<clsNovedadViaje>
{
    public void Configure(EntityTypeBuilder<clsNovedadViaje> builder)
    {
        // La novedad no se modifica ni se borra (triggers INSTEAD OF): EF no puede leer valores generados con OUTPUT.
        builder.ToTable("tblNovedadesViaje", t =>
        {
            t.HasTrigger("TR_tblNovedadesViaje_NoModificar");
            t.HasTrigger("TR_tblNovedadesViaje_NoBorrar");
        });
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.TipoCodigo).HasColumnType("varchar(30)").IsRequired();
        builder.Property(n => n.Rol).HasColumnType("varchar(10)").IsRequired();
        builder.Property(n => n.Proceso).HasColumnType("varchar(10)");
        builder.Property(n => n.Texto).HasMaxLength(clsNovedadViaje.TEXTO_MAX).IsRequired();
        builder.Property(n => n.Fecha).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(n => n.CreatedAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(n => n.FotoStoragePath).HasMaxLength(500);
        builder.Property(n => n.FotoSelloFecha).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(n => n.FotoDispositivoId).HasMaxLength(clsFotoNovedadViaje.DISPOSITIVO_MAX);
        builder.Property(n => n.FotoHashSha256).HasColumnType("char(64)");
        builder.Ignore(n => n.ConFoto);
        builder.Ignore(n => n.SinUbicacion);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(n => n.ViajeId).HasConstraintName("FK_tblNovedadesViaje_Viaje").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(n => n.UsuarioId).HasConstraintName("FK_tblNovedadesViaje_Usuario").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsLugar>().WithMany().HasForeignKey(n => n.LugarId).HasConstraintName("FK_tblNovedadesViaje_Lugar").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsTipoNovedadViaje>().WithMany().HasForeignKey(n => n.TipoCodigo).HasPrincipalKey(t => t.Codigo)
            .HasConstraintName("FK_tblNovedadesViaje_Tipo").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(n => new { n.ViajeId, n.IdEnvio }).IsUnique().HasFilter("[IdEnvio] IS NOT NULL").HasDatabaseName("UX_tblNovedadesViaje_Envio");
        builder.HasIndex(n => new { n.ViajeId, n.Fecha }).HasDatabaseName("IX_tblNovedadesViaje_Viaje");
    }
}
