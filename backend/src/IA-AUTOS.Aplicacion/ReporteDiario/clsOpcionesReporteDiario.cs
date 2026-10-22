namespace IAAutos.Aplicacion.ReporteDiario;

/// <summary>
/// Configuracion del proceso del reporte diario. La hora y los destinatarios no van aqui sino en los parametros de la base (<c>reporte_diario_hora</c> y <c>reporte_diario_destinatarios</c>):
/// aqui solo se apaga el proceso y se fijan los reintentos cuando el envio falla.
/// </summary>
public sealed class clsOpcionesReporteDiario
{
    public const string SECCION = "ReporteDiario";

    public bool ProcesoActivo { get; set; } = true;

    /// <summary>Cuantas veces se intenta enviar el reporte de un dia antes de darlo por fallido.</summary>
    public int IntentosMaximos { get; set; } = 3;

    /// <summary>Minutos de espera entre un intento fallido y el siguiente.</summary>
    public int ReintentoCadaMinutos { get; set; } = 15;
}
