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

internal sealed class clsConfiguracionClaseDano : IEntityTypeConfiguration<clsClaseDano>
{
    public void Configure(EntityTypeBuilder<clsClaseDano> builder) => clsConfiguracionCatalogo.Codificado(builder, "tblClasesDano");
}

internal sealed class clsConfiguracionTamanoDano : IEntityTypeConfiguration<clsTamanoDano>
{
    public void Configure(EntityTypeBuilder<clsTamanoDano> builder) => clsConfiguracionCatalogo.Codificado(builder, "tblTamanosDano");
}

internal sealed class clsConfiguracionNivelSeveridad : IEntityTypeConfiguration<clsNivelSeveridad>
{
    public void Configure(EntityTypeBuilder<clsNivelSeveridad> builder)
    {
        clsConfiguracionCatalogo.Codificado(builder, "tblNivelesSeveridad");
        builder.Property(n => n.Accion).HasMaxLength(clsNivelSeveridad.ACCION_MAX);
    }
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
        builder.ToTable("tblCuadrantes");
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
