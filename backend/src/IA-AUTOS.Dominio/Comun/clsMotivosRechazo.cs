namespace IAAutos.Dominio.Comun;

/// <summary>
/// Los motivos fijos de los rechazos (409, 403 y 404) de los viajes, los usuarios y la constancia de la transferencia: viajan en <c>details.motivo</c> junto al texto de siempre, con el mismo patron que
/// <c>ETAPA_PREVIA_SIN_CIERRE</c>, para que el portal y la app muestren el mensaje que corresponde sin leer la frase. Son valores estables en minusculas: el texto puede cambiar, el motivo no.
/// </summary>
public static class clsMotivosRechazo
{
    // ---- Viajes
    public const string VIAJE_NO_EXISTE = "viaje_no_existe";
    public const string VIAJE_NO_ASIGNADO = "viaje_no_asignado";
    public const string VIAJE_SIN_UNIDADES_DEL_CLIENTE = "viaje_sin_unidades_del_cliente";
    public const string VIAJE_CON_INSPECCIONES = "viaje_con_inspecciones";
    public const string TRANSICION_DE_ESTADO_INVALIDA = "transicion_de_estado_invalida";
    public const string CODIGO_MTRACK_DUPLICADO = "codigo_mtrack_duplicado";
    public const string CAPACIDAD_NO_BAJA = "capacidad_no_baja";
    public const string VEHICULO_NO_EXISTE = "vehiculo_no_existe";
    public const string VIN_FUERA_DEL_VIAJE = "vin_fuera_del_viaje";
    public const string VIN_YA_EN_EL_VIAJE = "vin_ya_en_el_viaje";
    public const string VIN_EN_OTRO_VIAJE_ACTIVO = "vin_en_otro_viaje_activo";
    public const string VIN_CON_INSPECCIONES = "vin_con_inspecciones";
    public const string DESCARGUE_CON_INFORME_CERRADO = "descargue_con_informe_cerrado";
    public const string SIN_SEGUNDA_INSPECCION_EN_CURSO = "sin_segunda_inspeccion_en_curso";
    public const string POSICION_OCUPADA = "posicion_ocupada";
    public const string CONDUCTOR_SIN_PERMISO_DE_EXCEPCION = "conductor_sin_permiso_de_excepcion";
    public const string OTRO_CLIENTE = "otro_cliente";

    public const string HALLAZGO_DE_VERSION_REEMPLAZADA = "hallazgo_de_version_reemplazada";

    // ---- El cargue por ninera
    public const string CARGUE_INCOMPLETO = "cargue_incompleto";
    public const string FIRMA_POR_NINERA = "firma_por_ninera";
    public const string ACOMODACION_CERRADA = "acomodacion_cerrada";

    // ---- La entrega por parada
    public const string SIN_RECEPTOR = "sin_receptor";
    public const string ENTREGA_TOMADA = "entrega_tomada";
    public const string CONDUCTOR_SIN_LLEGADA = "conductor_sin_llegada";
    public const string SIN_NOVEDAD_PARA_RECHAZAR = "sin_novedad_para_rechazar";
    public const string LLEGADA_ABIERTA = "llegada_abierta";
    public const string SIN_LLEGUE = "sin_llegue";
    public const string ENTREGA_POR_PARADA = "entrega_por_parada";
    public const string NO_ES_SU_PUNTO = "no_es_su_punto";

    // ---- La espera del inspector del punto
    public const string PARADA_SIN_INSPECTORES = "parada_sin_inspectores";
    public const string SIN_LLEGUE_EN_PARADA = "sin_llegue_en_parada";
    public const string ENTREGA_CERRADA = "entrega_cerrada";
    public const string PLAN_B_NO_HABILITADO = "plan_b_no_habilitado";
    public const string ENTREGA_CERRADA_SIN_INSPECTOR = "entrega_cerrada_sin_inspector";
    public const string LLEGADA_DEL_INSPECTOR_DEL_PUNTO = "llegada_del_inspector_del_punto";
    public const string DANOS_SIN_CALIFICAR = "danos_sin_calificar";

    // ---- Una sola revision en el descargue
    public const string SALIDA_DESCARGUE = "salida_descargue";

    // ---- No sale, vuelve a salir y bajar del viaje
    public const string CARGUE_FIRMADO = "cargue_firmado";
    public const string SIN_CUPO = "sin_cupo";
    public const string TIENE_REEMPLAZO = "tiene_reemplazo";
    public const string UNIDAD_NO_SALE = "unidad_no_sale";
    public const string UNIDAD_BAJADA = "unidad_bajada";
    public const string MOTIVO_NO_APLICA = "motivo_no_aplica";
    public const string FIRMANTES_DE_LA_TRANSFERENCIA = "firmantes_de_la_transferencia";

    // ---- Descartar un informe en borrador
    public const string SOLO_AUTOR_O_COORDINADOR = "solo_autor_o_coordinador";
    public const string INFORME_CERRADO = "informe_cerrado";
    public const string INFORME_CON_FIRMAS = "informe_con_firmas";
    public const string INFORME_DESCARTADO = "informe_descartado";

    // ---- Vehiculos
    public const string VEHICULO_EN_VIAJE_ACTIVO = "vehiculo_en_viaje_activo";

    // ---- Usuarios
    public const string USUARIO_NO_EXISTE = "usuario_no_existe";
    public const string USUARIO_PROPIO = "usuario_propio";
    public const string USUARIO_DE_OTRO_CLIENTE = "usuario_de_otro_cliente";
    public const string SOLO_SUPER_ADMIN_CAMBIA_CLIENTE = "solo_super_admin_cambia_cliente";
    public const string SOLO_SUPER_ADMIN_MODIFICA_SUPER_ADMIN = "solo_super_admin_modifica_super_admin";
    public const string SOLO_SUPER_ADMIN_ASIGNA_SUPER_ADMIN = "solo_super_admin_asigna_super_admin";
    public const string ROL_SIN_PERMISO_DE_PUNTOS = "rol_sin_permiso_de_puntos";
    public const string PUNTO_NO_ASIGNADO = "punto_no_asignado";

    // ---- Constancia de la transferencia
    public const string INSPECCION_NO_EXISTE = "inspeccion_no_existe";
    public const string INSPECCION_DE_OTRO_CLIENTE = "inspeccion_de_otro_cliente";
    public const string INSPECCION_SIN_TRANSFERENCIA = "inspeccion_sin_transferencia";

    /// <summary>El motivo de un viaje que ya no admite el cambio: <c>viaje_programado</c>, <c>viaje_en_curso</c>, <c>viaje_finalizado</c> o <c>viaje_cancelado</c> (el estado del viaje).</summary>
    public static string ViajeEn(string estado) => $"viaje_{estado}";

    /// <summary>El <c>details</c> de un rechazo con su motivo (<c>{ "motivo": "..." }</c>); nulo si no tiene.</summary>
    public static object? Detalles(string? motivo, IReadOnlyDictionary<string, object>? datos = null)
    {
        if (motivo is null)
        {
            return null;
        }

        var detalles = new Dictionary<string, object> { ["motivo"] = motivo };
        foreach (var (clave, valor) in datos ?? new Dictionary<string, object>())
        {
            detalles[clave] = valor;
        }

        return detalles;
    }
}
