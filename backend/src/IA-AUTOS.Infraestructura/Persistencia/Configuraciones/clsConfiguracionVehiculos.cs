using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
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
        builder.HasIndex(v => v.Activo).HasDatabaseName("IX_tblVehiculos_Activo");
        builder.HasIndex(v => v.Vin).IsUnique().HasDatabaseName("UQ_tblVehiculos_Vin");
        builder.HasOne<clsCliente>().WithMany().HasForeignKey(v => v.ClienteId)
            .HasConstraintName("FK_tblVehiculos_Cliente").OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasIndex(v => v.ClienteId).HasDatabaseName("IX_tblVehiculos_Cliente");
    }
}

internal sealed class clsConfiguracionEtapa : IEntityTypeConfiguration<clsEtapa>
{
    public void Configure(EntityTypeBuilder<clsEtapa> builder)
    {
        builder.ToTable("tblEtapas", t => t.HasTrigger("TR_tblEtapas_Completa"));
        clsConfiguracionBase.Base(builder);
        builder.HasOne<clsVehiculo>().WithMany().HasForeignKey(e => e.VehiculoId)
            .HasConstraintName("FK_tblEtapas_Vehiculo").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsEtapaCatalogo>().WithMany().HasForeignKey(e => e.EtapaCatalogoId)
            .HasConstraintName("FK_tblEtapas_EtapaCatalogo").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsPuntoCustodia>().WithMany().HasForeignKey(e => e.PuntoCustodiaId)
            .HasConstraintName("FK_tblEtapas_PuntoCustodia").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(e => e.ViajeId)
            .HasConstraintName("FK_tblEtapas_Viaje").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => new { e.ViajeId, e.VehiculoId, e.Orden }).IsUnique().HasDatabaseName("UQ_tblEtapas_Viaje_Vehiculo_Orden");
        builder.HasIndex(e => e.VehiculoId).HasDatabaseName("IX_tblEtapas_Vehiculo");
    }
}

internal sealed class clsConfiguracionUsuario : IEntityTypeConfiguration<clsUsuario>
{
    public void Configure(EntityTypeBuilder<clsUsuario> builder)
    {
        builder.ToTable("tblUsuarios");
        clsConfiguracionBase.Base(builder);
        builder.Property(u => u.Nombre).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.Rol).HasColumnType("varchar(30)").IsRequired();
        builder.Property(u => u.SubtipoInspector).HasColumnType("varchar(30)");
        builder.Property(u => u.PasswordHash).HasMaxLength(400);
        // Correo opcional (quien entra con cedula y PIN puede no tenerlo): unico entre quienes lo tienen.
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL").HasDatabaseName("UX_tblUsuarios_Email");

        // Id del portal: unico entre quienes lo tienen (los conductores no tienen usuario del portal y quedan en NULL).
        builder.Property(u => u.PortalUsuarioId).HasColumnType("bigint");
        builder.HasIndex(u => u.PortalUsuarioId).IsUnique().HasFilter("[PortalUsuarioId] IS NOT NULL").HasDatabaseName("UX_tblUsuarios_PortalUsuarioId");

        // Cedula: unica entre quienes la tienen (el conductor se identifica por ella en la carga de viajes).
        builder.Property(u => u.Cedula).HasColumnType("varchar(20)");
        builder.HasIndex(u => u.Cedula).IsUnique().HasFilter("[Cedula] IS NOT NULL").HasDatabaseName("UX_tblUsuarios_Cedula");

        // Credencial: con contrasena o con cedula y PIN; el bloqueo por PIN errados vive en la propia fila.
        builder.Property(u => u.TipoCredencial).HasColumnType("varchar(20)").IsRequired();
        builder.Property(u => u.PinHash).HasMaxLength(400);
        builder.Property(u => u.BloqueadoHasta).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(u => u.UltimoAccesoAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);

        // Cliente: el super administrador no tiene (ve todos).
        // ClientNoAction: EF no toca a los usuarios ya cargados en la solicitud; la base rechaza borrar un cliente en uso.
        builder.HasOne<clsCliente>().WithMany().HasForeignKey(u => u.ClienteId)
            .HasConstraintName("FK_tblUsuarios_Cliente").OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasIndex(u => u.ClienteId).HasDatabaseName("IX_tblUsuarios_Cliente");
    }
}
