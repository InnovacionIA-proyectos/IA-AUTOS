using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IAAutos.Infraestructura.Persistencia.Configuraciones;

internal sealed class clsConfiguracionNotaAclaratoria : IEntityTypeConfiguration<clsNotaAclaratoria>
{
    public void Configure(EntityTypeBuilder<clsNotaAclaratoria> builder)
    {
        // La nota no se modifica ni se borra (triggers INSTEAD OF): EF no puede leer valores generados con OUTPUT.
        builder.ToTable("tblNotasAclaratorias", t =>
        {
            t.HasTrigger("TR_tblNotasAclaratorias_NoModificar");
            t.HasTrigger("TR_tblNotasAclaratorias_NoBorrar");
        });
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Entidad).HasColumnType("varchar(15)").IsRequired();
        builder.Property(n => n.Texto).HasMaxLength(clsNotaAclaratoria.TEXTO_MAX).IsRequired();
        builder.Property(n => n.CreatedAt).HasColumnType(clsConfiguracionBase.TIPO_FECHA);
        builder.HasOne<clsUsuario>().WithMany().HasForeignKey(n => n.AutorId)
            .HasConstraintName("FK_tblNotasAclaratorias_Autor").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(n => new { n.Entidad, n.EntidadId }).HasDatabaseName("IX_tblNotasAclaratorias_Entidad");
    }
}
