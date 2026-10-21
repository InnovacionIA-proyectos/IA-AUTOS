using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionParametro : IEntityTypeConfiguration<clsParametro>
{
    public void Configure(EntityTypeBuilder<clsParametro> builder)
    {
        builder.ToTable("tblParametros");
        builder.HasKey(p => p.Clave);
        builder.Property(p => p.Clave).HasColumnType("varchar(100)").ValueGeneratedNever();
        builder.Property(p => p.Valor).HasMaxLength(1000);
        builder.Property(p => p.Tipo).HasColumnType("varchar(15)").IsRequired();
        builder.Property(p => p.Descripcion).HasMaxLength(300).IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(p => p.ActualizadoPor)
            .HasConstraintName("FK_tblParametros_ActualizadoPor").OnDelete(DeleteBehavior.NoAction);
    }
}
