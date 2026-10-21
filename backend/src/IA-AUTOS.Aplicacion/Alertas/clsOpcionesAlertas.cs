namespace IAAutos.Aplicacion.Alertas;

/// <summary>
/// Como corren los procesos periodicos de alertas (seccion <c>Alertas</c>): los avisos operativos (captura incompleta y transferencia pendiente) cada 10 minutos y los patrones cada hora. Los umbrales y
/// los minutos de espera no estan aqui: son parametros que se editan sin desplegar. <c>ProcesosActivos</c> en falso los apaga (por ejemplo, en una instancia que solo atiende peticiones).
/// </summary>
public sealed class clsOpcionesAlertas
{
    public const string SECCION = "Alertas";

    public bool ProcesosActivos { get; set; } = true;

    public int AvisosCadaMinutos { get; set; } = 10;

    public int UmbralesCadaMinutos { get; set; } = 60;
}
