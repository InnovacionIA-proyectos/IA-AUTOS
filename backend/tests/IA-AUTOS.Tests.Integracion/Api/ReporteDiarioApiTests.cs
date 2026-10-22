using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Exportaciones;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Aplicacion.ReporteDiario;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El reporte diario por correo: se envia a la hora configurada (hora de Colombia) con la tabla en el cuerpo y el Excel de hallazgos del dia, trae las mismas cifras que el indicador del tablero,
/// queda registrado como notificacion de correo (enviado, fallido o sin destinatarios) y no se repite el mismo dia.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed partial class ReporteDiarioApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private sealed class RelojFijo(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
    }

    private readonly clsEnviadorCorreoFalso _correo = new();

    [GeneratedRegex("<row ")]
    private static partial Regex FilaXml();

    /// <summary>El instante, en hora de Colombia, del dia siguiente a <paramref name="dia"/> a la hora indicada.</summary>
    private static DateTimeOffset DiaSiguienteA(string dia, string hora) =>
        DateTimeOffset.Parse($"{dia}T{hora}:00-05:00", CultureInfo.InvariantCulture).AddDays(1);

    /// <summary>El servicio del reporte con el correo falso y un reloj fijo; el resto es el de la aplicacion.</summary>
    private async Task<clsResultadoReporteDiario> EjecutarAsync(RelojFijo reloj, int intentosMaximos = 3)
    {
        using var alcance = Fabrica.Services.CreateScope();
        var s = alcance.ServiceProvider;
        var servicio = new clsReporteDiarioService(
            s.GetRequiredService<clsReportesTableroService>(),
            s.GetRequiredService<clsExportacionesService>(),
            s.GetRequiredService<IParametros>(),
            s.GetRequiredService<INotificacionRepository>(),
            _correo,
            s.GetRequiredService<IUnidadDeTrabajo>(),
            Options.Create(new clsOpcionesReporteDiario { IntentosMaximos = intentosMaximos }),
            reloj,
            NullLogger<clsReporteDiarioService>.Instance);
        return await servicio.EjecutarAsync();
    }

    /// <summary>Las notificaciones no se borran (son evidencia): la prueba apaga sus triggers solo para empezar de cero.</summary>
    private Task BorrarNotificacionesAsync() =>
        Sembrador.EjecutarAsync("ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL; DELETE FROM dbo.tblNotificaciones; ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL");

    private Task<int> FilasDelReporteAsync() => Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'reporte_diario' AND Canal = 'correo'");

    private async Task<string?> CampoAsync(string columna, string clave) =>
        (await Sembrador.EscalarAsync($"SELECT CAST({columna} AS nvarchar(1000)) FROM dbo.tblNotificaciones WHERE ClaveDedupe = @k", ("@k", clave))) as string;

    private static int FilasDelExcel(clsAdjuntoCorreo adjunto)
    {
        using var zip = new ZipArchive(new MemoryStream(adjunto.Contenido), ZipArchiveMode.Read);
        using var lector = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return FilaXml().Count(lector.ReadToEnd());
    }

    // --------------------------------------------------------------------------------------------- el envio

    [Fact]
    public async Task El_reporte_va_a_los_destinatarios_con_la_tabla_y_el_excel_del_dia_y_queda_registrado()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com, comercial@ejemplo.com");
        var reloj = new RelojFijo(DiaSiguienteA(e.Dias.Dia, "07:00"));

        var resultado = await EjecutarAsync(reloj);

        Assert.Equal(EstadoReporteDiario.Enviado, resultado.Estado);
        Assert.True(resultado.Terminado);
        Assert.Equal(e.Dias.Dia, resultado.Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var correo = Assert.Single(_correo.Enviados);
        Assert.Equal(["calidad@ejemplo.com", "comercial@ejemplo.com"], correo.Destinatarios);
        Assert.Equal($"Reporte diario IA Autos {e.Dias.Dia}", correo.Asunto);
        Assert.Equal($"ia-autos-reporte-diario-{e.Dias.Dia}-intento-1", correo.IdCorrelacion);
        Assert.Contains("<table", correo.CuerpoHtml);
        Assert.Contains("nacional", correo.CuerpoHtml);
        Assert.Contains("importacion", correo.CuerpoHtml);
        Assert.Contains("Otro cliente", correo.CuerpoHtml);
        Assert.DoesNotContain("rechazo", correo.CuerpoHtml, StringComparison.OrdinalIgnoreCase);

        // El Excel adjunto es el de los hallazgos de ese dia (los tres del escenario) y lleva el dia en el nombre.
        var adjunto = Assert.Single(correo.Adjuntos);
        Assert.Equal($"hallazgos-{e.Dias.Dia}.xlsx", adjunto.Nombre);
        Assert.Equal(clsExportacionesService.TIPO_XLSX, adjunto.TipoContenido);
        Assert.Equal(1 + 3, FilasDelExcel(adjunto));

        // Una fila de correo, enviada, sin destinatario ni error, con la clave del dia.
        var clave = $"reporte_diario:{e.Dias.Dia}";
        Assert.Equal(1, await FilasDelReporteAsync());
        Assert.NotNull(await CampoAsync("EnviadaAt", clave));
        Assert.Null(await CampoAsync("ErrorEnvio", clave));
        Assert.Null(await CampoAsync("UsuarioId", clave));
        Assert.Equal($"Reporte diario IA Autos {e.Dias.Dia}", await CampoAsync("Titulo", clave));
    }

    [Fact]
    public async Task El_correo_trae_los_mismos_valores_que_el_indicador_diario_del_tablero()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);
        var indicador = await ConsultarAsync(superAdmin, "/api/reportes/indicador-diario", $"?fecha={e.Dias.Dia}");
        var tiempo = await ConsultarAsync(superAdmin, "/api/reportes/tiempo-captura", $"?desde={e.Dias.Dia}&hasta={e.Dias.Dia}");

        await EjecutarAsync(new RelojFijo(DiaSiguienteA(e.Dias.Dia, "06:00")));

        var correo = Assert.Single(_correo.Enviados);
        var despachados = indicador.GetProperty("despachados").GetInt32();
        var conNovedad = indicador.GetProperty("con_novedad").GetInt32();
        var porcentaje = indicador.GetProperty("porcentaje").GetDecimal();
        Assert.Equal((4, 2, 50m), (despachados, conNovedad, porcentaje));
        var mediana = tiempo.GetProperty("mediana_minutos").GetDecimal();
        var resumen = await CampoAsync("Mensaje", $"reporte_diario:{e.Dias.Dia}");
        Assert.StartsWith($"Día {e.Dias.Dia}: {despachados} despachados, {conNovedad} con novedad ({clsCorreoReporteDiario.Texto(porcentaje)} %), objetivo 1,2 %.", resumen);
        Assert.Contains($"Mediana de captura: {clsCorreoReporteDiario.Texto(mediana)} min.", resumen);
        Assert.Contains($"<td style=\"border:1px solid #c9ced6;padding:6px 10px;text-align:right;\">{despachados}</td>", correo.CuerpoHtml);
        Assert.Contains($"{clsCorreoReporteDiario.Texto(mediana)} minutos", correo.CuerpoHtml);

        // Las filas por operacion y por cliente son las del tablero.
        foreach (var operacion in indicador.GetProperty("por_operacion").EnumerateArray())
        {
            Assert.Contains($">{operacion.GetProperty("operacion").GetString()}</td>", correo.CuerpoHtml);
            Assert.Contains($">{clsCorreoReporteDiario.Texto(operacion.GetProperty("porcentaje").GetDecimal())}</td>", correo.CuerpoHtml);
        }

        foreach (var cliente in indicador.GetProperty("por_cliente").EnumerateArray())
        {
            Assert.Contains($">{cliente.GetProperty("cliente_nombre").GetString()}</td>", correo.CuerpoHtml);
        }
    }

    [Fact]
    public async Task Un_dia_sin_despachos_tambien_se_reporta_con_ceros_y_el_excel_solo_con_titulos()
    {
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");

        var resultado = await EjecutarAsync(new RelojFijo(DateTimeOffset.Parse("2026-03-10T08:00:00-05:00", CultureInfo.InvariantCulture)));

        Assert.Equal(EstadoReporteDiario.Enviado, resultado.Estado);
        var correo = Assert.Single(_correo.Enviados);
        Assert.Equal("Reporte diario IA Autos 2026-03-09", correo.Asunto);
        Assert.Contains("Sin unidades despachadas ese día.", correo.CuerpoHtml);
        Assert.Contains("No hubo inspecciones de Cargue o Descargue cerradas ese día.", correo.CuerpoHtml);
        Assert.Equal(1, FilasDelExcel(Assert.Single(correo.Adjuntos)));
    }

    // ------------------------------------------------------------------------------------ cuando se envia

    [Fact]
    public async Task No_se_envia_antes_de_la_hora_configurada_y_si_a_partir_de_ella()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        await CambiarParametroAsync("reporte_diario_hora", "09:30");
        var reloj = new RelojFijo(DiaSiguienteA(e.Dias.Dia, "09:29"));

        Assert.Equal(EstadoReporteDiario.AunNoEsLaHora, (await EjecutarAsync(reloj)).Estado);
        Assert.Empty(_correo.Enviados);
        Assert.Equal(0, await FilasDelReporteAsync());

        reloj.Ahora = DiaSiguienteA(e.Dias.Dia, "09:30");
        Assert.Equal(EstadoReporteDiario.Enviado, (await EjecutarAsync(reloj)).Estado);
        Assert.Single(_correo.Enviados);
    }

    [Fact]
    public async Task La_hora_se_cuenta_en_hora_de_Colombia_y_no_en_UTC()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        // Son las 05:30 en Colombia (10:30 UTC): aun no son las 06:00 aunque en UTC ya pasaron.
        var reloj = new RelojFijo(DiaSiguienteA(e.Dias.Dia, "05:30"));

        Assert.Equal(EstadoReporteDiario.AunNoEsLaHora, (await EjecutarAsync(reloj)).Estado);
        Assert.Empty(_correo.Enviados);
    }

    [Fact]
    public async Task El_mismo_dia_no_se_envia_dos_veces()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        var reloj = new RelojFijo(DiaSiguienteA(e.Dias.Dia, "07:00"));

        await EjecutarAsync(reloj);
        reloj.Ahora = DiaSiguienteA(e.Dias.Dia, "18:00");
        var otraVez = await EjecutarAsync(reloj);

        Assert.Equal(EstadoReporteDiario.YaProcesado, otraVez.Estado);
        Assert.True(otraVez.Terminado);
        Assert.Single(_correo.Enviados);
        Assert.Equal(1, await FilasDelReporteAsync());
    }

    // ------------------------------------------------------------------------------- sin destinatarios

    [Fact]
    public async Task Sin_destinatarios_no_se_envia_nada_y_queda_registrado_que_el_reporte_no_se_envio()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        var reloj = new RelojFijo(DiaSiguienteA(e.Dias.Dia, "06:00"));

        var resultado = await EjecutarAsync(reloj);

        Assert.Equal(EstadoReporteDiario.SinDestinatarios, resultado.Estado);
        Assert.True(resultado.Terminado);
        Assert.Empty(_correo.Enviados);
        var clave = $"reporte_diario:{e.Dias.Dia}";
        Assert.Equal(1, await FilasDelReporteAsync());
        Assert.Null(await CampoAsync("EnviadaAt", clave));
        Assert.Equal(clsReporteDiarioService.SIN_DESTINATARIOS, await CampoAsync("ErrorEnvio", clave));

        // Ese dia queda cerrado: aunque se configuren destinatarios despues, no se manda el reporte atrasado.
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        Assert.Equal(EstadoReporteDiario.YaProcesado, (await EjecutarAsync(reloj)).Estado);
        Assert.Empty(_correo.Enviados);
    }

    // ------------------------------------------------------------------------------- fallos del envio

    [Fact]
    public async Task Un_envio_fallido_queda_registrado_con_su_error_y_se_reintenta_con_una_fila_nueva()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        var reloj = new RelojFijo(DiaSiguienteA(e.Dias.Dia, "06:05"));
        _correo.Respuesta = clsResultadoCorreo.Fallo("MailSender respondió 503: servicio no disponible");

        var primero = await EjecutarAsync(reloj);

        Assert.Equal(EstadoReporteDiario.Fallo, primero.Estado);
        Assert.False(primero.Terminado);
        var intento1 = $"reporte_diario:{e.Dias.Dia}:intento-1";
        Assert.Null(await CampoAsync("EnviadaAt", intento1));
        Assert.Equal("MailSender respondió 503: servicio no disponible", await CampoAsync("ErrorEnvio", intento1));

        _correo.Respuesta = null;
        reloj.Ahora = DiaSiguienteA(e.Dias.Dia, "06:25");
        var segundo = await EjecutarAsync(reloj);

        Assert.Equal(EstadoReporteDiario.Enviado, segundo.Estado);
        Assert.Equal(2, _correo.Enviados.Count);
        Assert.Equal(2, await FilasDelReporteAsync());
        Assert.NotNull(await CampoAsync("EnviadaAt", $"reporte_diario:{e.Dias.Dia}"));
        Assert.Equal(EstadoReporteDiario.YaProcesado, (await EjecutarAsync(reloj)).Estado);
    }

    [Fact]
    public async Task Al_agotar_los_intentos_el_dia_se_da_por_fallido_y_no_se_sigue_intentando()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        var reloj = new RelojFijo(DiaSiguienteA(e.Dias.Dia, "06:05"));
        _correo.Respuesta = clsResultadoCorreo.Fallo("MailSender respondió 503: servicio no disponible");

        var primero = await EjecutarAsync(reloj, intentosMaximos: 2);
        var ultimo = await EjecutarAsync(reloj, intentosMaximos: 2);
        var despues = await EjecutarAsync(reloj, intentosMaximos: 2);

        Assert.False(primero.Terminado);
        Assert.Equal(EstadoReporteDiario.Fallo, ultimo.Estado);
        Assert.True(ultimo.Terminado);
        Assert.Equal(EstadoReporteDiario.YaProcesado, despues.Estado);
        Assert.Equal(2, _correo.Enviados.Count);
        Assert.Equal(2, await FilasDelReporteAsync());
    }

    [Fact]
    public async Task Un_cliente_de_correo_que_lanza_no_rompe_el_proceso_y_el_fallo_queda_registrado()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        _correo.Lanza = true;

        var resultado = await EjecutarAsync(new RelojFijo(DiaSiguienteA(e.Dias.Dia, "06:05")));

        Assert.Equal(EstadoReporteDiario.Fallo, resultado.Estado);
        Assert.Contains("El servidor de correo no responde", await CampoAsync("ErrorEnvio", $"reporte_diario:{e.Dias.Dia}:intento-1"));
    }

    [Fact]
    public async Task Una_hora_invalida_en_el_parametro_se_trata_como_las_seis_de_la_manana()
    {
        var e = await EscenarioAsync();
        await BorrarNotificacionesAsync();
        await CambiarParametroAsync("reporte_diario_destinatarios", "calidad@ejemplo.com");
        await CambiarParametroAsync("reporte_diario_hora", "pronto");

        Assert.Equal(EstadoReporteDiario.AunNoEsLaHora, (await EjecutarAsync(new RelojFijo(DiaSiguienteA(e.Dias.Dia, "05:59")))).Estado);
        Assert.Equal(EstadoReporteDiario.Enviado, (await EjecutarAsync(new RelojFijo(DiaSiguienteA(e.Dias.Dia, "06:00")))).Estado);
    }
}
