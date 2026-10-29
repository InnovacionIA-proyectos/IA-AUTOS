using IAAutos.Dominio.Catalogos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

/// <summary>Mapeo comun de los catalogos con codigo: tabla, codigo unico en minusculas y nombre de hasta 100 caracteres.</summary>
internal static class clsConfiguracionCatalogo
{
    public static void Codificado<T>(EntityTypeBuilder<T> builder, string tabla)
        where T : clsCatalogoCodificado
    {
        builder.ToTable(tabla);
        clsConfiguracionBase.Base(builder);
        builder.Property(c => c.Codigo).HasColumnType("varchar(30)").IsRequired();
        builder.Property(c => c.Nombre).HasMaxLength(clsCatalogoBase.NOMBRE_MAX).IsRequired();
        builder.HasIndex(c => c.Codigo).IsUnique().HasDatabaseName($"UQ_{tabla}_Codigo");
    }
}

internal sealed class clsConfiguracionEtapaCatalogo : IEntityTypeConfiguration<clsEtapaCatalogo>
{
    public void Configure(EntityTypeBuilder<clsEtapaCatalogo> builder) => clsConfiguracionCatalogo.Codificado(builder, "tblEtapasCatalogo");
}

internal sealed class clsConfiguracionPuntoCustodia : IEntityTypeConfiguration<clsPuntoCustodia>
{
    public void Configure(EntityTypeBuilder<clsPuntoCustodia> builder) => clsConfiguracionCatalogo.Codificado(builder, "tblPuntosCustodia");
}

internal sealed class clsConfiguracionTipoUnidad : IEntityTypeConfiguration<clsTipoUnidadTransportadora>
{
    public void Configure(EntityTypeBuilder<clsTipoUnidadTransportadora> builder) =>
        clsConfiguracionCatalogo.Codificado(builder, "tblTiposUnidadTransportadora");
}

internal sealed class clsConfiguracionCliente : IEntityTypeConfiguration<clsCliente>
{
    public void Configure(EntityTypeBuilder<clsCliente> builder) => clsConfiguracionCatalogo.Codificado(builder, "tblClientes");
}

internal sealed class clsConfiguracionClaseDano : IEntityTypeConfiguration<clsClaseDano>
{
    public void Configure(EntityTypeBuilder<clsClaseDano> builder) => clsConfiguracionCatalogo.Codificado(builder, "tblClasesDano");
}

internal sealed class clsConfiguracionTamanoDano : IEntityTypeConfiguration<clsTamanoDano>
{
    public void Configure(EntityTypeBuilder<clsTamanoDano> builder) => clsConfiguracionCatalogo.Codificado(builder, "tblTamanosDano");
}

internal sealed class clsConfiguracionEstadoSofasa : IEntityTypeConfiguration<clsEstadoSofasa>
{
    public void Configure(EntityTypeBuilder<clsEstadoSofasa> builder)
    {
        builder.ToTable("tblEstadosSofasa");
        clsConfiguracionBase.Base(builder);
        // El codigo es el StatusCode real de la orden (texto libre, con sus mayusculas): no se limita como el de los demas catalogos.
        builder.Property(e => e.Codigo).HasMaxLength(clsEstadoSofasa.CODIGO_MAX).IsRequired();
        builder.Property(e => e.Nombre).HasMaxLength(clsCatalogoBase.NOMBRE_MAX).IsRequired();
        builder.HasIndex(e => e.Codigo).IsUnique().HasDatabaseName("UQ_tblEstadosSofasa_Codigo");
    }
}

internal sealed class clsConfiguracionNivelSeveridad : IEntityTypeConfiguration<clsNivelSeveridad>
{
    public void Configure(EntityTypeBuilder<clsNivelSeveridad> builder)
    {
        clsConfiguracionCatalogo.Codificado(builder, "tblNivelesSeveridad");
        builder.Property(n => n.Accion).HasMaxLength(clsNivelSeveridad.ACCION_MAX);
    }
}

internal sealed class clsConfiguracionLugar : IEntityTypeConfiguration<clsLugar>
{
    public void Configure(EntityTypeBuilder<clsLugar> builder)
    {
        builder.ToTable("tblLugares");
        clsConfiguracionBase.Base(builder);
        builder.Property(l => l.Nombre).HasMaxLength(clsCatalogoBase.NOMBRE_MAX).IsRequired();
        builder.Property(l => l.Ciudad).HasMaxLength(clsLugar.CIUDAD_MAX).IsRequired();
        builder.HasOne<clsPuntoCustodia>().WithMany().HasForeignKey(l => l.TipoId)
            .HasConstraintName("FK_tblLugares_Tipo").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(l => l.Nombre).IsUnique().HasDatabaseName("UQ_tblLugares_Nombre");
        builder.HasIndex(l => l.TipoId).HasDatabaseName("IX_tblLugares_Tipo");
    }
}

internal sealed class clsConfiguracionMotivoSinInspector : IEntityTypeConfiguration<clsMotivoSinInspector>
{
    public void Configure(EntityTypeBuilder<clsMotivoSinInspector> builder) => clsConfiguracionCatalogo.Codificado(builder, "tblMotivosSinInspector");
}

internal sealed class clsConfiguracionVistaFoto : IEntityTypeConfiguration<clsVistaFoto>
{
    public void Configure(EntityTypeBuilder<clsVistaFoto> builder)
    {
        clsConfiguracionCatalogo.Codificado(builder, "tblVistasFoto");
        builder.Property(v => v.TipoVehiculo).HasColumnType("varchar(30)");
    }
}

internal sealed class clsConfiguracionCuadrante : IEntityTypeConfiguration<clsCuadrante>
{
    public void Configure(EntityTypeBuilder<clsCuadrante> builder)
    {
        // La base crea las zonas de cada cuadrante nuevo con un trigger (TR_tblCuadrantes_CrearZonas).
        builder.ToTable("tblCuadrantes", t => t.HasTrigger("TR_tblCuadrantes_CrearZonas"));
        clsConfiguracionBase.Base(builder);
        builder.Property(c => c.Nombre).HasMaxLength(clsCatalogoBase.NOMBRE_MAX).IsRequired();
        builder.Property(c => c.TipoVehiculo).HasColumnType("varchar(30)");
        builder.HasIndex(c => new { c.TipoVehiculo, c.Numero }).IsUnique().HasDatabaseName("UQ_tblCuadrantes_Tipo_Numero");
    }
}

/// <summary>Un cuadrante se dibuja una vez por vista; al borrar el cuadrante se borran sus poligonos, pero una vista con poligonos no se borra.</summary>
internal sealed class clsConfiguracionCuadranteVista : IEntityTypeConfiguration<clsCuadranteVista>
{
    public void Configure(EntityTypeBuilder<clsCuadranteVista> builder)
    {
        builder.ToTable("tblCuadranteVistas");
        clsConfiguracionBase.Base(builder);
        builder.Property(c => c.Geometria).HasColumnType("nvarchar(max)").IsRequired();
        builder.HasOne<clsCuadrante>().WithMany().HasForeignKey(c => c.CuadranteId)
            .HasConstraintName("FK_tblCuadranteVistas_Cuadrante").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<clsVistaFoto>().WithMany().HasForeignKey(c => c.VistaFotoId)
            .HasConstraintName("FK_tblCuadranteVistas_Vista").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(c => new { c.CuadranteId, c.VistaFotoId }).IsUnique().HasDatabaseName("UQ_tblCuadranteVistas_Cuadrante_Vista");
        builder.HasIndex(c => c.VistaFotoId).HasDatabaseName("IX_tblCuadranteVistas_Vista");
    }
}

/// <summary>Los cinco paneles de la ficha del vehiculo donde se toca la region del dano; cada uno con su imagen de fondo.</summary>
internal sealed class clsConfiguracionPanelDano : IEntityTypeConfiguration<clsPanelDano>
{
    public void Configure(EntityTypeBuilder<clsPanelDano> builder)
    {
        clsConfiguracionCatalogo.Codificado(builder, "tblPanelesDano");
        builder.Property(p => p.Codigo).HasColumnType("varchar(40)");
        builder.Property(p => p.ImagenPath).HasMaxLength(clsPanelDano.IMAGEN_PATH_MAX);
    }
}

/// <summary>Las zonas del dano: cada cuadrante numerado con su lado, mas las zonas provisionales. Solo lectura desde la aplicacion (las crea la base).</summary>
internal sealed class clsConfiguracionZonaDano : IEntityTypeConfiguration<clsZonaDano>
{
    public void Configure(EntityTypeBuilder<clsZonaDano> builder)
    {
        builder.ToTable("tblZonasDano");
        clsConfiguracionBase.Base(builder);
        builder.Property(z => z.Lado).HasColumnType("varchar(10)");
        builder.Property(z => z.Codigo).HasColumnType("varchar(40)");
        builder.Property(z => z.Nombre).HasMaxLength(clsCatalogoBase.NOMBRE_MAX);
        builder.HasIndex(z => new { z.CuadranteId, z.Lado }).IsUnique().HasDatabaseName("UX_tblZonasDano_Cuadrante_Lado");
        builder.HasIndex(z => z.Codigo).IsUnique().HasDatabaseName("UX_tblZonasDano_Codigo");
    }
}

/// <summary>El poligono de una zona numerada en un panel: una zona se dibuja una vez por panel y al borrar la zona se borran sus regiones.</summary>
internal sealed class clsConfiguracionRegionDano : IEntityTypeConfiguration<clsRegionDano>
{
    public void Configure(EntityTypeBuilder<clsRegionDano> builder)
    {
        builder.ToTable("tblRegionesDano");
        clsConfiguracionBase.Base(builder);
        builder.Property(r => r.Geometria).HasColumnType("nvarchar(max)").IsRequired();
        builder.HasOne<clsPanelDano>().WithMany().HasForeignKey(r => r.PanelId)
            .HasConstraintName("FK_tblRegionesDano_Panel").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(r => new { r.PanelId, r.ZonaId }).IsUnique().HasDatabaseName("UQ_tblRegionesDano_Panel_Zona");
        builder.HasIndex(r => r.ZonaId).HasDatabaseName("IX_tblRegionesDano_Zona");
    }
}

