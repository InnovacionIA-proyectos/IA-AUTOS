using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionMapeoCargaViajes : IEntityTypeConfiguration<clsMapeoCargaViajes>
{
    public void Configure(EntityTypeBuilder<clsMapeoCargaViajes> builder)
    {
        builder.ToTable("tblMapeosCargaViajes");
        clsConfiguracionBase.Base(builder);
        builder.Property(m => m.Huella).HasColumnType("char(64)").IsRequired();
        builder.Property(m => m.Columnas).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(m => m.Campos).HasColumnType("nvarchar(max)").IsRequired();
        builder.HasOne<clsCliente>().WithMany().HasForeignKey(m => m.ClienteId).HasConstraintName("FK_tblMapeosCargaViajes_Cliente").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(m => m.ActualizadoPor).HasConstraintName("FK_tblMapeosCargaViajes_ActualizadoPor").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(m => new { m.ClienteId, m.Huella }).IsUnique().HasDatabaseName("UQ_tblMapeosCargaViajes_Cliente_Huella");
    }
}
