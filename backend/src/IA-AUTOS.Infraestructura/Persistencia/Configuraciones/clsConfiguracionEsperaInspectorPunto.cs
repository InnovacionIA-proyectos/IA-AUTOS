using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionEsperaInspectorPunto : IEntityTypeConfiguration<clsEsperaInspectorPunto>
{
    public void Configure(EntityTypeBuilder<clsEsperaInspectorPunto> builder)
    {
        builder.ToTable("tblEsperasInspectorPunto");
        clsConfiguracionBase.Base(builder);
        builder.Property(e => e.MotivoCodigo).HasColumnType("varchar(30)").IsRequired();
        builder.Property(e => e.MotivoTexto).HasMaxLength(clsEsperaInspectorPunto.TEXTO_MAX);
        builder.Property(e => e.ActivadaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        // El estado es el testigo de concurrencia: si un inspector toma la entrega y el conductor cierra con el plan B a la vez, la base deja pasar a uno y el otro recibe el conflicto.
        builder.Property(e => e.Estado).HasColumnType("varchar(40)").IsRequired().IsConcurrencyToken();
        builder.Property(e => e.AvisoAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(e => e.InspectorLlegoAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(e => e.CerradaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Ignore(e => e.EstaEsperando);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(e => e.ViajeId).HasConstraintName("FK_tblEsperasInspectorPunto_Viaje").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsLugar>().WithMany().HasForeignKey(e => e.LugarId).HasConstraintName("FK_tblEsperasInspectorPunto_Lugar").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(e => e.ConductorId).HasConstraintName("FK_tblEsperasInspectorPunto_Conductor").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(e => e.InspectorId).HasConstraintName("FK_tblEsperasInspectorPunto_Inspector").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(e => e.CerradaPor).HasConstraintName("FK_tblEsperasInspectorPunto_CerradaPor").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsMotivoSinInspector>().WithMany().HasForeignKey(e => e.MotivoCodigo).HasPrincipalKey(m => m.Codigo)
            .HasConstraintName("FK_tblEsperasInspectorPunto_Motivo").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => new { e.ViajeId, e.LugarId }).IsUnique().HasFilter("[Estado] = 'esperando'").HasDatabaseName("UX_tblEsperasInspectorPunto_Abierta");
        builder.HasIndex(e => new { e.ViajeId, e.LugarId, e.ActivadaAt }).HasDatabaseName("IX_tblEsperasInspectorPunto_Parada");
        builder.HasIndex(e => new { e.Estado, e.ActivadaAt }).HasDatabaseName("IX_tblEsperasInspectorPunto_Estado");
    }
}
