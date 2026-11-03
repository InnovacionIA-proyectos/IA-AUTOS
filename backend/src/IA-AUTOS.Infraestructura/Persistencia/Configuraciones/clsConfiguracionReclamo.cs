using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionReclamo : IEntityTypeConfiguration<clsReclamo>
{
    public void Configure(EntityTypeBuilder<clsReclamo> builder)
    {
        // El reclamo es historial y no se borra (un trigger lo impide): EF no puede leer valores generados con OUTPUT.
        builder.ToTable("tblReclamos", t => t.HasTrigger("TR_tblReclamos_NoBorrar"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Proceso).HasColumnType("varchar(10)").IsRequired();
        builder.Property(r => r.ReclamadoAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(r => r.LiberadoAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(r => r.ViajeId).HasConstraintName("FK_tblReclamos_Viaje").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(r => r.InspectorId).HasConstraintName("FK_tblReclamos_Inspector").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(r => r.LiberadoPor).HasConstraintName("FK_tblReclamos_LiberadoPor").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(r => r.ViajeId).HasDatabaseName("IX_tblReclamos_Viaje");
        builder.HasIndex(r => r.InspectorId).HasDatabaseName("IX_tblReclamos_Inspector");
        builder.HasIndex(r => new { r.ViajeId, r.Proceso }).IsUnique().HasFilter("[LiberadoAt] IS NULL").HasDatabaseName("UX_tblReclamos_Activo");
    }
}

internal sealed class clsConfiguracionInspectorLugar : IEntityTypeConfiguration<clsInspectorLugar>
{
    public void Configure(EntityTypeBuilder<clsInspectorLugar> builder)
    {
        builder.ToTable("tblInspectorLugares");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.CreatedAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(p => p.UsuarioId).HasConstraintName("FK_tblInspectorLugares_Usuario").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsLugar>().WithMany().HasForeignKey(p => p.LugarId).HasConstraintName("FK_tblInspectorLugares_Lugar").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(p => new { p.UsuarioId, p.LugarId }).IsUnique().HasDatabaseName("UQ_tblInspectorLugares_Par");
        builder.HasIndex(p => p.LugarId).HasDatabaseName("IX_tblInspectorLugares_Lugar");
    }
}
