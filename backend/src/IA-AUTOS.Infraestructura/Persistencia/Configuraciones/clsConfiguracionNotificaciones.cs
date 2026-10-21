using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionNotificacion : IEntityTypeConfiguration<clsNotificacion>
{
    public void Configure(EntityTypeBuilder<clsNotificacion> builder)
    {
        // Solo se lee y se responde (un trigger rechaza cualquier otro cambio) y no se borra: EF no puede leer valores generados con OUTPUT.
        builder.ToTable("tblNotificaciones", t =>
        {
            t.HasTrigger("TR_tblNotificaciones_SoloLecturaYRespuesta");
            t.HasTrigger("TR_tblNotificaciones_NoBorrar");
        });
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Tipo).HasColumnType("varchar(30)").IsRequired();
        builder.Property(n => n.Urgencia).HasColumnType("varchar(5)").IsRequired();
        builder.Property(n => n.Titulo).HasMaxLength(clsNotificacion.TITULO_MAX).IsRequired();
        builder.Property(n => n.Mensaje).HasMaxLength(clsNotificacion.MENSAJE_MAX).IsRequired();
        builder.Property(n => n.Canal).HasColumnType("varchar(10)").IsRequired();
        builder.Property(n => n.EnviadaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(n => n.ErrorEnvio).HasMaxLength(clsNotificacion.ERROR_MAX);
        builder.Property(n => n.LeidaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(n => n.RespuestaTexto).HasMaxLength(clsNotificacion.RESPUESTA_MAX);
        builder.Property(n => n.RespuestaMedio).HasColumnType("varchar(10)");
        builder.Property(n => n.RespondidaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(n => n.ClaveDedupe).HasColumnType("varchar(300)").IsRequired();
        builder.Property(n => n.CreatedAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Ignore(n => n.EstaLeida);
        builder.Ignore(n => n.EstaRespondida);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(n => n.UsuarioId)
            .HasConstraintName("FK_tblNotificaciones_Usuario").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(n => n.RespondidaPor)
            .HasConstraintName("FK_tblNotificaciones_RespondidaPor").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(n => n.ViajeId)
            .HasConstraintName("FK_tblNotificaciones_Viaje").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(n => n.VehiculoId)
            .HasConstraintName("FK_tblNotificaciones_Vehiculo").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsHallazgo>().WithMany().HasForeignKey(n => n.HallazgoId)
            .HasConstraintName("FK_tblNotificaciones_Hallazgo").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsInspeccion>().WithMany().HasForeignKey(n => n.InspeccionId)
            .HasConstraintName("FK_tblNotificaciones_Inspeccion").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(n => n.ClaveDedupe).IsUnique().HasDatabaseName("UQ_tblNotificaciones_ClaveDedupe");
        builder.HasIndex(n => new { n.UsuarioId, n.LeidaAt }).HasDatabaseName("IX_tblNotificaciones_Usuario_Leida");
    }
}

internal sealed class clsConfiguracionDispositivo : IEntityTypeConfiguration<clsDispositivo>
{
    public void Configure(EntityTypeBuilder<clsDispositivo> builder)
    {
        builder.ToTable("tblDispositivos");
        clsConfiguracionBase.Base(builder);
        builder.Property(d => d.TokenFcm).HasColumnType("varchar(512)").IsRequired();
        builder.Property(d => d.Plataforma).HasColumnType("varchar(20)").IsRequired();
        builder.Property(d => d.Modelo).HasMaxLength(clsDispositivo.MODELO_MAX);
        builder.Property(d => d.UltimoUsoAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(d => d.UsuarioId)
            .HasConstraintName("FK_tblDispositivos_Usuario").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(d => d.TokenFcm).IsUnique().HasDatabaseName("UQ_tblDispositivos_TokenFcm");
        builder.HasIndex(d => new { d.UsuarioId, d.Activo }).HasDatabaseName("IX_tblDispositivos_Usuario");
    }
}
