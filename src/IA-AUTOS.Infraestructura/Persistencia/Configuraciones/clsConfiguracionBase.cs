using IAAutos.Dominio.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

/// <summary>Mapeo comun: clave asignada por el dominio y marcas de tiempo como datetimeoffset(3).</summary>
internal static class clsConfiguracionBase
{
    public const string TIPO_FECHA = "datetimeoffset(3)";

    public static void Base<T>(EntityTypeBuilder<T> builder)
        where T : clsEntidadBase
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CreatedAt).HasColumnType(TIPO_FECHA);
        builder.Property(e => e.UpdatedAt).HasColumnType(TIPO_FECHA);
    }
}
