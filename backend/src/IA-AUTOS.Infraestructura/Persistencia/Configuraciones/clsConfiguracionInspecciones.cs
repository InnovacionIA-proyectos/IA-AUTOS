using IAAutos.Dominio.Catalogos;
using System.Text.Json;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionInspeccion : IEntityTypeConfiguration<clsInspeccion>
{
    public void Configure(EntityTypeBuilder<clsInspeccion> builder)
    {
        // El trigger de inmutabilidad se declara para que EF no use OUTPUT directo en las actualizaciones.
        builder.ToTable("tblInspecciones", t => t.HasTrigger("TR_tblInspecciones_Inmutable"));
        clsConfiguracionBase.Base(builder);
        builder.Property(i => i.Momento).HasColumnType("varchar(20)").IsRequired();
        builder.Property(i => i.Fecha).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(i => i.Estado).HasColumnType("varchar(20)").IsRequired();
        builder.Property(i => i.ConductorNombre).HasMaxLength(200);
        builder.Property(i => i.Placa).HasMaxLength(50);
        builder.Property(i => i.Trailer).HasMaxLength(50);
        builder.Property(i => i.Notas).HasMaxLength(1000);
        builder.Property(i => i.IniciadaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(i => i.CerradaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsInspeccion>().WithMany().HasForeignKey(i => i.CorrigeInspeccionId)
            .HasConstraintName("FK_tblInspecciones_Corrige").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsEtapa>().WithMany().HasForeignKey(i => i.EtapaId)
            .HasConstraintName("FK_tblInspecciones_Etapa").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsViaje>().WithMany().HasForeignKey(i => i.ViajeId)
            .HasConstraintName("FK_tblInspecciones_Viaje").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(i => i.UsuarioId)
            .HasConstraintName("FK_tblInspecciones_Usuario").OnDelete(DeleteBehavior.NoAction);
        // Una sola inspeccion original por etapa y momento; las correcciones (versiones siguientes) no cuentan, y cada version se corrige una vez.
        builder.HasIndex(i => new { i.EtapaId, i.Momento }).IsUnique().HasFilter("[CorrigeInspeccionId] IS NULL")
            .HasDatabaseName("UX_tblInspecciones_Etapa_Momento_Original");
        builder.HasIndex(i => i.CorrigeInspeccionId).IsUnique().HasFilter("[CorrigeInspeccionId] IS NOT NULL")
            .HasDatabaseName("UX_tblInspecciones_Corrige");
    }
}

internal sealed class clsConfiguracionHallazgo : IEntityTypeConfiguration<clsHallazgo>
{
    private static readonly ValueConverter<clsCoordsDiagrama?, string?> CONVERSOR_COORDS = new(
        coords => Serializar(coords),
        json => LeerCoords(json));

    private static string? Serializar(clsCoordsDiagrama? coords) =>
        coords is null ? null : JsonSerializer.Serialize(new { x = coords.X, y = coords.Y });

    public void Configure(EntityTypeBuilder<clsHallazgo> builder)
    {
        builder.ToTable("tblHallazgos", t => t.HasTrigger("TR_tblHallazgos_NoEnCerrada"));
        // La clase y el tamano se guardan como codigo y referencian al catalogo (un valor en uso no se puede borrar).
        builder.HasOne<clsClaseDano>().WithMany().HasForeignKey(h => h.ClaseDano).HasPrincipalKey(c => c.Codigo)
            .HasConstraintName("FK_tblHallazgos_ClaseDano").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsTamanoDano>().WithMany().HasForeignKey(h => h.TamanoDano).HasPrincipalKey(c => c.Codigo)
            .HasConstraintName("FK_tblHallazgos_TamanoDano").OnDelete(DeleteBehavior.NoAction);
        clsConfiguracionBase.Base(builder);
        builder.Property(h => h.ParteVehiculo).HasMaxLength(200).IsRequired();
        builder.Property(h => h.ClaseDano).HasColumnType("varchar(30)").IsRequired();
        builder.Property(h => h.TamanoDano).HasColumnType("varchar(30)").IsRequired();
        builder.Property(h => h.CoordsDiagrama).HasConversion(CONVERSOR_COORDS).HasMaxLength(200);
        builder.HasOne<clsInspeccion>().WithMany().HasForeignKey(h => h.InspeccionId)
            .HasConstraintName("FK_tblHallazgos_Inspeccion").OnDelete(DeleteBehavior.NoAction);
    }

    private static clsCoordsDiagrama? LeerCoords(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;
        return new clsCoordsDiagrama(
            raiz.GetProperty("x").GetDouble(),
            raiz.GetProperty("y").GetDouble());
    }
}

internal sealed class clsConfiguracionEvidencia : IEntityTypeConfiguration<clsEvidencia>
{
    public void Configure(EntityTypeBuilder<clsEvidencia> builder)
    {
        builder.ToTable("tblEvidencias", t => t.HasTrigger("TR_tblEvidencias_NoEnCerrada"));
        clsConfiguracionBase.Base(builder);
        builder.Property(e => e.StoragePath).HasMaxLength(500).IsRequired();
        builder.Property(e => e.SelloFecha).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(e => e.SelloServidorAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(e => e.HashSha256).HasColumnType("char(64)");
        builder.Property(e => e.DispositivoId).HasMaxLength(clsEvidencia.DISPOSITIVO_MAX);
        builder.Property(e => e.Nota).HasMaxLength(clsEvidencia.NOTA_MAX);
        builder.HasOne<clsVistaFoto>().WithMany().HasForeignKey(e => e.VistaId)
            .HasConstraintName("FK_tblEvidencias_Vista").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => e.VistaId).HasDatabaseName("IX_tblEvidencias_Vista");
        builder.Property(e => e.EsConstancia).HasDefaultValue(false);
        builder.HasOne<clsInspeccion>().WithMany().HasForeignKey(e => e.InspeccionId)
            .HasConstraintName("FK_tblEvidencias_Inspeccion").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsHallazgo>().WithMany().HasForeignKey(e => e.HallazgoId)
            .HasConstraintName("FK_tblEvidencias_Hallazgo").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class clsConfiguracionTransferencia : IEntityTypeConfiguration<clsTransferenciaCustodia>
{
    public void Configure(EntityTypeBuilder<clsTransferenciaCustodia> builder)
    {
        builder.ToTable("tblTransferenciasCustodia");
        clsConfiguracionBase.Base(builder);
        builder.Property(t => t.FirmadaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(t => t.FirmaEntregaAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(t => t.FirmaRecibeAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.Property(t => t.FirmaEntregaIp).HasColumnType("varchar(45)");
        builder.Property(t => t.FirmaRecibeIp).HasColumnType("varchar(45)");
        builder.Property(t => t.TipoCierre).HasColumnType("varchar(20)");
        builder.Property(t => t.NoFirmaParte).HasColumnType("varchar(10)");
        builder.Property(t => t.ReservasTexto).HasMaxLength(clsTransferenciaCustodia.RESERVAS_MAX);
        builder.Property(t => t.MotivoNoFirma).HasMaxLength(clsTransferenciaCustodia.MOTIVO_MAX);
        // El testigo y la foto de la constancia: ningun borrado en cascada (la inspeccion arrastra la transferencia y la foto a la vez).
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(t => t.TestigoUsuarioId)
            .HasConstraintName("FK_tblTransferencias_Testigo").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsEvidencia>().WithMany().HasForeignKey(t => t.EvidenciaConstanciaId)
            .HasConstraintName("FK_tblTransferencias_Constancia").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsInspeccion>().WithMany().HasForeignKey(t => t.InspeccionId)
            .HasConstraintName("FK_tblTransferencias_Inspeccion").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(t => t.EntregaUsuarioId)
            .HasConstraintName("FK_tblTransferencias_Entrega").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(t => t.RecibeUsuarioId)
            .HasConstraintName("FK_tblTransferencias_Recibe").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(t => t.InspeccionId).IsUnique().HasDatabaseName("UQ_tblTransferencias_Inspeccion");
    }
}
