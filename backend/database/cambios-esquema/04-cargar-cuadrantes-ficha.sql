/* =============================================================================
   Carga los cuadrantes de la ficha del vehiculo generico: 36 zonas numeradas y 97 poligonos (cada zona se dibuja en las vistas
   donde aparece). Se ejecuta DESPUES de 03-cuadrantes-por-zona.sql y se puede repetir: lo que ya existe no se duplica ni se pisa.

   Que carga:
     - tblCuadrantes: una fila por zona (numero y nombre), sin tipo de vehiculo (aplica a cualquiera).
     - tblCuadranteVistas: el poligono de cada zona en cada una de las vistas obligatorias donde aparece
       (superior, lateral izquierda, lateral derecha, diagonal trasera izquierda y diagonal delantera derecha).
     - Borra la fila de ejemplo "Frontal 1" de la plantilla de pruebas si todavia existe.

   Los numeros 4 y 12 no existen en la ficha, y las vistas frontal y trasera no tienen cuadrantes dibujados.
   Los nombres son una lectura de la imagen: calidad debe validarlos y se corrigen desde el portal (PATCH /api/catalogos/cuadrantes/{id}).
   Los puntos son proporciones (0 a 1) de la imagen de cada vista, con origen en la esquina superior izquierda.

   Como ejecutarlo: abrir en SSMS (o sqlcmd) CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar. Todo ocurre en una transaccion.
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

/* ---------------------------------- PARAMETRO ---------------------------------- */
DECLARE @esquema sysname = N'db_IA_AUTOS';
/* ------------------------------------------------------------------------------- */

DECLARE @q nvarchar(300) = QUOTENAME(@esquema);

IF SCHEMA_ID(@esquema) IS NULL
    THROW 50000, N'El esquema indicado en @esquema no existe en esta base.', 1;
IF OBJECT_ID(@q + N'.tblCuadranteVistas', N'U') IS NULL OR OBJECT_ID(@q + N'.tblVistasFoto', N'U') IS NULL
    THROW 50001, N'Falta tblCuadranteVistas: ejecute antes 03-cuadrantes-por-zona.sql (y revise @esquema).', 1;

-- Las tablas temporales viven en la sesion: si un intento anterior quedo a medias en la misma conexion, se descartan primero.
-- Sus columnas de texto usan la collation de la base (no la de tempdb) para poder compararlas con las tablas de IA Autos.
DROP TABLE IF EXISTS #zona_ficha;
DROP TABLE IF EXISTS #poligono_ficha;

CREATE TABLE #zona_ficha (Numero int NOT NULL PRIMARY KEY, Nombre nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL);
INSERT #zona_ficha (Numero, Nombre) VALUES
    (1, N'Puerta delantera: parte alta'),
    (2, N'Larguero de techo delantero'),
    (3, N'Pilar central'),
    (5, N'Puerta delantera: media, borde delantero'),
    (6, N'Puerta delantera: media, centro'),
    (7, N'Puerta delantera: media, borde trasero'),
    (8, N'Puerta delantera: baja, borde delantero'),
    (9, N'Puerta delantera: baja, centro'),
    (10, N'Puerta delantera: baja, borde trasero'),
    (11, N'Puerta trasera: parte alta'),
    (13, N'Larguero de techo trasero'),
    (14, N'Pilar trasero'),
    (15, N'Manija de puerta trasera'),
    (16, N'Puerta trasera: media, borde delantero'),
    (17, N'Puerta trasera: media, centro'),
    (18, N'Puerta trasera: media, borde trasero'),
    (19, N'Puerta trasera: baja, borde delantero'),
    (20, N'Puerta trasera: baja, centro'),
    (21, N'Puerta trasera: baja, borde trasero'),
    (22, N'Port' + NCHAR(243) + N'n trasero: cuadrante superior exterior'),
    (23, N'Port' + NCHAR(243) + N'n trasero: cuadrante superior interior'),
    (24, N'Port' + NCHAR(243) + N'n trasero: cuadrante inferior exterior'),
    (25, N'Port' + NCHAR(243) + N'n trasero: cuadrante inferior interior'),
    (26, N'Aler' + NCHAR(243) + N'n / borde trasero del techo'),
    (27, N'Techo: centro'),
    (28, N'Techo: lateral derecho'),
    (29, N'Techo: lateral izquierdo'),
    (30, N'Cap' + NCHAR(243) + N': junto al parabrisas, centro'),
    (31, N'Cap' + NCHAR(243) + N': junto al parabrisas, lado derecho'),
    (32, N'Cap' + NCHAR(243) + N': junto al parabrisas, lado izquierdo'),
    (33, N'Cap' + NCHAR(243) + N': franja media, centro'),
    (34, N'Cap' + NCHAR(243) + N': franja media, lado derecho'),
    (35, N'Cap' + NCHAR(243) + N': franja media, lado izquierdo'),
    (36, N'Cap' + NCHAR(243) + N': borde delantero, centro'),
    (37, N'Cap' + NCHAR(243) + N': borde delantero, lado derecho'),
    (38, N'Cap' + NCHAR(243) + N': borde delantero, lado izquierdo');

CREATE TABLE #poligono_ficha (Vista varchar(30) COLLATE DATABASE_DEFAULT NOT NULL, Numero int NOT NULL, Geometria nvarchar(max) COLLATE DATABASE_DEFAULT NOT NULL, PRIMARY KEY (Vista, Numero));
INSERT #poligono_ficha (Vista, Numero, Geometria) VALUES
    ('superior', 26, N'{"puntos":[[0.8669,0.2247],[0.8669,0.4712],[0.8932,0.474],[0.889,0.5014],[0.8669,0.5014],[0.8669,0.7644],[0.8738,0.7644],[0.8835,0.7151],[0.8918,0.6274],[0.896,0.4438],[0.8849,0.2603],[0.8779,0.2247]]}'),
    ('superior', 27, N'{"puntos":[[0.4313,0.3753],[0.4313,0.6027],[0.4355,0.6493],[0.8558,0.6438],[0.8571,0.3753]]}'),
    ('superior', 28, N'{"puntos":[[0.4411,0.2822],[0.853,0.2986],[0.8488,0.2274],[0.8405,0.2192],[0.5798,0.2192],[0.4577,0.2027]]}'),
    ('superior', 29, N'{"puntos":[[0.853,0.6712],[0.4411,0.6932],[0.4577,0.7644],[0.5687,0.7507],[0.8474,0.7479]]}'),
    ('superior', 30, N'{"puntos":[[0.2705,0.263],[0.2025,0.2685],[0.1956,0.4301],[0.1969,0.6027],[0.2025,0.6959],[0.2705,0.7014],[0.2566,0.537],[0.2566,0.4219]]}'),
    ('superior', 31, N'{"puntos":[[0.2067,0.1315],[0.2039,0.2575],[0.2746,0.2493],[0.2913,0.1808],[0.2788,0.1288]]}'),
    ('superior', 32, N'{"puntos":[[0.2039,0.7068],[0.2067,0.8329],[0.2788,0.8329],[0.2899,0.7836],[0.2705,0.7123]]}'),
    ('superior', 33, N'{"puntos":[[0.1969,0.2685],[0.1415,0.274],[0.1345,0.3781],[0.1331,0.537],[0.1429,0.6932],[0.1969,0.6959],[0.1914,0.5781],[0.1914,0.3863]]}'),
    ('superior', 34, N'{"puntos":[[0.2011,0.1315],[0.1498,0.1397],[0.1429,0.263],[0.1983,0.2575]]}'),
    ('superior', 35, N'{"puntos":[[0.1429,0.7014],[0.1498,0.8219],[0.2011,0.8329],[0.1983,0.7068]]}'),
    ('superior', 36, N'{"puntos":[[0.0735,0.2767],[0.0624,0.3479],[0.0555,0.4658],[0.0583,0.5699],[0.0721,0.6849],[0.1359,0.6904],[0.129,0.5616],[0.129,0.4027],[0.1373,0.274]]}'),
    ('superior', 37, N'{"puntos":[[0.1456,0.1397],[0.1318,0.1425],[0.1207,0.1589],[0.0777,0.2685],[0.1387,0.263]]}'),
    ('superior', 38, N'{"puntos":[[0.0777,0.6959],[0.0957,0.7534],[0.1276,0.8192],[0.1456,0.8247],[0.1387,0.7014]]}'),
    ('diagonal_trasera_izquierda', 1, N'{"puntos":[[0.3481,0.2906],[0.165,0.3105],[0.1498,0.3732],[0.2913,0.3732],[0.2996,0.3618],[0.3356,0.3732]]}'),
    ('diagonal_trasera_izquierda', 2, N'{"puntos":[[0.3315,0.0769],[0.3024,0.0912],[0.2483,0.1567],[0.2455,0.2308],[0.3315,0.2308]]}'),
    ('diagonal_trasera_izquierda', 3, N'{"puntos":[[0.387,0.0655],[0.3786,0.0655],[0.3398,0.2678],[0.3454,0.2821],[0.3551,0.2678]]}'),
    ('diagonal_trasera_izquierda', 5, N'{"puntos":[[0.1928,0.4274],[0.1456,0.433],[0.1526,0.5726],[0.1928,0.5726]]}'),
    ('diagonal_trasera_izquierda', 6, N'{"puntos":[[0.2552,0.4188],[0.1969,0.4274],[0.1969,0.5726],[0.251,0.5726]]}'),
    ('diagonal_trasera_izquierda', 7, N'{"puntos":[[0.3315,0.4131],[0.2607,0.4188],[0.2566,0.5755],[0.3245,0.5783]]}'),
    ('diagonal_trasera_izquierda', 8, N'{"puntos":[[0.154,0.584],[0.165,0.6809],[0.1983,0.6838],[0.1928,0.584]]}'),
    ('diagonal_trasera_izquierda', 9, N'{"puntos":[[0.1969,0.584],[0.2039,0.6866],[0.2566,0.6923],[0.251,0.584]]}'),
    ('diagonal_trasera_izquierda', 10, N'{"puntos":[[0.2552,0.584],[0.2621,0.6923],[0.3301,0.7009],[0.3232,0.5869]]}'),
    ('diagonal_trasera_izquierda', 11, N'{"puntos":[[0.38,0.3219],[0.5479,0.2991],[0.5437,0.3818],[0.38,0.3846]]}'),
    ('diagonal_trasera_izquierda', 13, N'{"puntos":[[0.4882,0.0399],[0.5548,0.0456],[0.6352,0.057],[0.6366,0.0883],[0.4938,0.0684]]}'),
    ('diagonal_trasera_izquierda', 14, N'{"puntos":[[0.5534,0.0883],[0.6283,0.1225],[0.6255,0.3191],[0.5423,0.3162],[0.5465,0.2336],[0.5617,0.114]]}'),
    ('diagonal_trasera_izquierda', 16, N'{"puntos":[[0.337,0.416],[0.3287,0.5783],[0.3717,0.5812],[0.3773,0.4131]]}'),
    ('diagonal_trasera_izquierda', 17, N'{"puntos":[[0.43,0.4103],[0.3828,0.4131],[0.3786,0.584],[0.4244,0.5869]]}'),
    ('diagonal_trasera_izquierda', 18, N'{"puntos":[[0.5257,0.4046],[0.4355,0.4103],[0.43,0.5869],[0.4771,0.5926],[0.4896,0.5242],[0.5201,0.4758]]}'),
    ('diagonal_trasera_izquierda', 19, N'{"puntos":[[0.3287,0.5897],[0.3356,0.7009],[0.3773,0.7066],[0.3731,0.5954]]}'),
    ('diagonal_trasera_izquierda', 20, N'{"puntos":[[0.3786,0.5926],[0.3828,0.7094],[0.4286,0.7151],[0.4244,0.5983]]}'),
    ('diagonal_trasera_izquierda', 21, N'{"puntos":[[0.4286,0.5983],[0.4341,0.7151],[0.473,0.7208],[0.4743,0.604]]}'),
    ('diagonal_trasera_izquierda', 22, N'{"puntos":[[0.8585,0.3134],[0.8655,0.4217],[0.9362,0.4188],[0.9209,0.3191]]}'),
    ('diagonal_trasera_izquierda', 23, N'{"puntos":[[0.853,0.3134],[0.7573,0.3276],[0.749,0.3846],[0.7351,0.4074],[0.7351,0.4302],[0.8613,0.4245]]}'),
    ('diagonal_trasera_izquierda', 24, N'{"puntos":[[0.939,0.4302],[0.8696,0.433],[0.8752,0.5242],[0.9459,0.5157],[0.9459,0.4615]]}'),
    ('diagonal_trasera_izquierda', 25, N'{"puntos":[[0.871,0.5271],[0.8627,0.433],[0.7323,0.4416],[0.7559,0.5242],[0.767,0.5413]]}'),
    ('diagonal_trasera_izquierda', 26, N'{"puntos":[[0.6408,0.057],[0.7032,0.0399],[0.8211,0.0484],[0.853,0.0741],[0.8558,0.1111],[0.7046,0.094],[0.6394,0.1197]]}'),
    ('diagonal_delantera_derecha', 1, N'{"puntos":[[0.635,0.2877],[0.6462,0.3675],[0.6615,0.3675],[0.6657,0.359],[0.6867,0.3675],[0.8308,0.3675],[0.814,0.302],[0.8084,0.302],[0.807,0.3134]]}'),
    ('diagonal_delantera_derecha', 2, N'{"puntos":[[0.6783,0.1026],[0.7357,0.1225],[0.7762,0.1937],[0.786,0.245],[0.6769,0.245]]}'),
    ('diagonal_delantera_derecha', 3, N'{"puntos":[[0.5888,0.0769],[0.607,0.0769],[0.628,0.2707],[0.6112,0.2707]]}'),
    ('diagonal_delantera_derecha', 5, N'{"puntos":[[0.7888,0.4274],[0.7888,0.5726],[0.8308,0.5726],[0.835,0.433]]}'),
    ('diagonal_delantera_derecha', 6, N'{"puntos":[[0.7245,0.4188],[0.7301,0.5755],[0.7832,0.5726],[0.7846,0.4274]]}'),
    ('diagonal_delantera_derecha', 7, N'{"puntos":[[0.6503,0.4131],[0.6587,0.5783],[0.7245,0.5755],[0.7189,0.4188]]}'),
    ('diagonal_delantera_derecha', 8, N'{"puntos":[[0.7888,0.584],[0.7832,0.6866],[0.821,0.6838],[0.8294,0.584]]}'),
    ('diagonal_delantera_derecha', 9, N'{"puntos":[[0.7301,0.584],[0.7245,0.6952],[0.7762,0.6895],[0.7832,0.584]]}'),
    ('diagonal_delantera_derecha', 10, N'{"puntos":[[0.6587,0.5897],[0.6531,0.7037],[0.7189,0.6952],[0.7245,0.5869]]}'),
    ('diagonal_delantera_derecha', 11, N'{"puntos":[[0.4336,0.3162],[0.6462,0.2991],[0.6462,0.3875],[0.442,0.3818]]}'),
    ('diagonal_delantera_derecha', 13, N'{"puntos":[[0.5245,0.037],[0.6294,0.0427],[0.6573,0.0627],[0.6503,0.0826],[0.5245,0.0684]]}'),
    ('diagonal_delantera_derecha', 14, N'{"puntos":[[0.4364,0.0883],[0.4615,0.094],[0.4657,0.2422],[0.4462,0.2336]]}'),
    ('diagonal_delantera_derecha', 16, N'{"puntos":[[0.6056,0.4103],[0.6098,0.5812],[0.6545,0.5783],[0.6462,0.416]]}'),
    ('diagonal_delantera_derecha', 17, N'{"puntos":[[0.5552,0.4103],[0.5594,0.5869],[0.6056,0.584],[0.6,0.4131]]}'),
    ('diagonal_delantera_derecha', 18, N'{"puntos":[[0.4601,0.4017],[0.4643,0.4758],[0.4811,0.4929],[0.4951,0.5214],[0.5091,0.5926],[0.5552,0.5869],[0.5497,0.4074]]}'),
    ('diagonal_delantera_derecha', 19, N'{"puntos":[[0.6531,0.5897],[0.6098,0.5954],[0.6056,0.7094],[0.6476,0.7037]]}'),
    ('diagonal_delantera_derecha', 20, N'{"puntos":[[0.6042,0.5926],[0.5594,0.5983],[0.5566,0.7151],[0.6,0.7094]]}'),
    ('diagonal_delantera_derecha', 21, N'{"puntos":[[0.5552,0.5983],[0.5105,0.604],[0.5133,0.7208],[0.551,0.7151]]}'),
    ('diagonal_delantera_derecha', 22, N'{"puntos":[[0.0671,0.3191],[0.0615,0.3732],[0.0503,0.3989],[0.0503,0.416],[0.1259,0.4188],[0.1315,0.3134]]}'),
    ('diagonal_delantera_derecha', 23, N'{"puntos":[[0.1357,0.3134],[0.1315,0.4245],[0.2587,0.4274],[0.2601,0.4103],[0.2448,0.4017],[0.2364,0.3818],[0.228,0.3248]]}'),
    ('diagonal_delantera_derecha', 24, N'{"puntos":[[0.049,0.4274],[0.0406,0.4644],[0.042,0.5128],[0.1161,0.5242],[0.1231,0.433]]}'),
    ('diagonal_delantera_derecha', 25, N'{"puntos":[[0.1217,0.5242],[0.2224,0.5385],[0.2545,0.4387],[0.1273,0.433]]}'),
    ('diagonal_delantera_derecha', 26, N'{"puntos":[[0.2378,0.0627],[0.2895,0.0456],[0.3497,0.0484],[0.3552,0.0997],[0.3077,0.1197],[0.2378,0.1083]]}'),
    ('lateral_izquierda', 1, N'{"puntos":[[0.3121,0.3309],[0.5603,0.295],[0.5603,0.4245],[0.3121,0.4424]]}'),
    ('lateral_izquierda', 2, N'{"puntos":[[0.3467,0.2158],[0.4646,0.0791],[0.5645,0.0576],[0.5617,0.1187],[0.4646,0.1367],[0.3745,0.2374]]}'),
    ('lateral_izquierda', 3, N'{"puntos":[[0.5687,0.0647],[0.5992,0.0647],[0.5936,0.295],[0.5603,0.2986]]}'),
    ('lateral_izquierda', 5, N'{"puntos":[[0.3883,0.4281],[0.3024,0.4424],[0.3051,0.6043],[0.3897,0.5899]]}'),
    ('lateral_izquierda', 6, N'{"puntos":[[0.4716,0.4173],[0.3939,0.4281],[0.3953,0.5899],[0.4716,0.5791]]}'),
    ('lateral_izquierda', 7, N'{"puntos":[[0.5534,0.4065],[0.4771,0.4173],[0.4757,0.5791],[0.5562,0.5719]]}'),
    ('lateral_izquierda', 8, N'{"puntos":[[0.3897,0.6043],[0.3051,0.6187],[0.3107,0.7446],[0.3911,0.741]]}'),
    ('lateral_izquierda', 9, N'{"puntos":[[0.4702,0.5935],[0.3953,0.6007],[0.3967,0.741],[0.4702,0.741]]}'),
    ('lateral_izquierda', 10, N'{"puntos":[[0.5576,0.5827],[0.4757,0.5899],[0.4757,0.7374],[0.5562,0.7374]]}'),
    ('lateral_izquierda', 11, N'{"puntos":[[0.5603,0.295],[0.7767,0.2878],[0.7767,0.4137],[0.5603,0.4245]]}'),
    ('lateral_izquierda', 13, N'{"puntos":[[0.5978,0.0612],[0.7601,0.0719],[0.7836,0.1007],[0.7739,0.1439],[0.5964,0.1259]]}'),
    ('lateral_izquierda', 14, N'{"puntos":[[0.7628,0.1007],[0.8266,0.1835],[0.8682,0.2878],[0.785,0.2878],[0.7739,0.1439]]}'),
    ('lateral_izquierda', 15, N'{"puntos":[[0.7074,0.3165],[0.749,0.3165],[0.7628,0.3453],[0.7628,0.3849],[0.749,0.4137],[0.7074,0.4137],[0.6935,0.3849],[0.6935,0.3453]]}'),
    ('lateral_izquierda', 16, N'{"puntos":[[0.6241,0.3957],[0.552,0.4065],[0.5548,0.5719],[0.6227,0.5683]]}'),
    ('lateral_izquierda', 17, N'{"puntos":[[0.6879,0.3849],[0.6283,0.3921],[0.6269,0.5683],[0.6865,0.5719]]}'),
    ('lateral_izquierda', 18, N'{"puntos":[[0.7836,0.3741],[0.6935,0.3849],[0.6907,0.5719],[0.7323,0.5719],[0.7573,0.4856],[0.7822,0.4496]]}'),
    ('lateral_izquierda', 19, N'{"puntos":[[0.5617,0.5827],[0.5617,0.7374],[0.6214,0.7338],[0.6227,0.5791]]}'),
    ('lateral_izquierda', 20, N'{"puntos":[[0.6269,0.5791],[0.6269,0.7338],[0.6838,0.7338],[0.6865,0.5827]]}'),
    ('lateral_izquierda', 21, N'{"puntos":[[0.6907,0.5827],[0.6893,0.7338],[0.7171,0.7338],[0.7295,0.5827]]}'),
    ('lateral_derecha', 1, N'{"puntos":[[0.5133,0.3022],[0.6797,0.3129],[0.6811,0.4388],[0.5119,0.4173]]}'),
    ('lateral_derecha', 2, N'{"puntos":[[0.4266,0.0647],[0.5105,0.0719],[0.5874,0.1583],[0.6811,0.3273],[0.6545,0.3309],[0.579,0.1978],[0.5105,0.1223],[0.428,0.1151]]}'),
    ('lateral_derecha', 3, N'{"puntos":[[0.386,0.0576],[0.4196,0.0612],[0.428,0.2698],[0.386,0.259]]}'),
    ('lateral_derecha', 5, N'{"puntos":[[0.5986,0.4317],[0.5972,0.5935],[0.6783,0.6079],[0.6811,0.446]]}'),
    ('lateral_derecha', 6, N'{"puntos":[[0.5119,0.4173],[0.5133,0.5827],[0.5916,0.5935],[0.5944,0.4317]]}'),
    ('lateral_derecha', 7, N'{"puntos":[[0.4224,0.4065],[0.4266,0.5719],[0.5077,0.5791],[0.5077,0.4173]]}'),
    ('lateral_derecha', 8, N'{"puntos":[[0.5972,0.6043],[0.5944,0.7446],[0.6727,0.7446],[0.6769,0.6187]]}'),
    ('lateral_derecha', 9, N'{"puntos":[[0.5133,0.5935],[0.5133,0.741],[0.5902,0.741],[0.5916,0.6043]]}'),
    ('lateral_derecha', 10, N'{"puntos":[[0.428,0.5827],[0.428,0.7374],[0.5077,0.741],[0.5077,0.5935]]}'),
    ('lateral_derecha', 11, N'{"puntos":[[0.1986,0.259],[0.3888,0.2446],[0.5133,0.3022],[0.5119,0.4173],[0.1986,0.3777]]}'),
    ('lateral_derecha', 13, N'{"puntos":[[0.2378,0.0647],[0.386,0.0576],[0.386,0.1151],[0.2587,0.1223],[0.2098,0.1295]]}'),
    ('lateral_derecha', 14, N'{"puntos":[[0.1301,0.0576],[0.2378,0.0647],[0.2098,0.1295],[0.1986,0.2626],[0.1301,0.2626]]}'),
    ('lateral_derecha', 15, N'{"puntos":[[0.1469,0.3022],[0.1888,0.3022],[0.2028,0.3309],[0.2028,0.3705],[0.1888,0.3993],[0.1469,0.3993],[0.1329,0.3705],[0.1329,0.3309]]}'),
    ('lateral_derecha', 16, N'{"puntos":[[0.3622,0.3957],[0.3622,0.5683],[0.4224,0.5719],[0.4182,0.4065]]}'),
    ('lateral_derecha', 17, N'{"puntos":[[0.2951,0.3885],[0.2979,0.5719],[0.358,0.5683],[0.3566,0.3957]]}'),
    ('lateral_derecha', 18, N'{"puntos":[[0.1986,0.3741],[0.2014,0.4532],[0.2294,0.4928],[0.2503,0.5719],[0.2937,0.5719],[0.2909,0.3849]]}'),
    ('lateral_derecha', 19, N'{"puntos":[[0.3622,0.5791],[0.3636,0.7338],[0.4224,0.7374],[0.4224,0.5827]]}'),
    ('lateral_derecha', 20, N'{"puntos":[[0.2979,0.5827],[0.2993,0.7338],[0.358,0.7338],[0.358,0.5791]]}'),
    ('lateral_derecha', 21, N'{"puntos":[[0.2545,0.5827],[0.2657,0.7302],[0.2951,0.7338],[0.2937,0.5827]]}');

DECLARE @faltan varchar(30), @sqlVistas nvarchar(max) = REPLACE(N'
    SELECT TOP (1) @vista = p.Vista
      FROM (SELECT DISTINCT Vista FROM #poligono_ficha) p
     WHERE NOT EXISTS (SELECT 1 FROM {S}.tblVistasFoto v WHERE v.Codigo = p.Vista AND v.Obligatoria = 1)', N'{S}', @q);
EXEC sp_executesql @sqlVistas, N'@vista varchar(30) OUTPUT', @vista = @faltan OUTPUT;
IF @faltan IS NOT NULL
BEGIN
    DECLARE @mensaje nvarchar(300) = CONCAT(N'Falta la vista obligatoria ', @faltan, N' en tblVistasFoto: revise los codigos de las vistas.');
    THROW 50010, @mensaje, 1;
END

DECLARE @carga nvarchar(max) = REPLACE(N'
    -- Fila de ejemplo de la plantilla de pruebas (sus poligonos se borran en cascada).
    DELETE FROM {S}.tblCuadrantes WHERE TipoVehiculo IS NULL AND Numero = 1 AND Nombre = N~Frontal 1~;

    INSERT INTO {S}.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre, Orden)
    SELECT NEWID(), NULL, z.Numero, z.Nombre, z.Numero
      FROM #zona_ficha z
     WHERE NOT EXISTS (SELECT 1 FROM {S}.tblCuadrantes c WHERE c.TipoVehiculo IS NULL AND c.Numero = z.Numero);

    INSERT INTO {S}.tblCuadranteVistas (Id, CuadranteId, VistaFotoId, Geometria)
    SELECT NEWID(), c.Id, v.Id, p.Geometria
      FROM #poligono_ficha p
      JOIN {S}.tblCuadrantes c ON c.TipoVehiculo IS NULL AND c.Numero = p.Numero
      JOIN {S}.tblVistasFoto v ON v.Codigo = p.Vista
     WHERE NOT EXISTS (SELECT 1 FROM {S}.tblCuadranteVistas x WHERE x.CuadranteId = c.Id AND x.VistaFotoId = v.Id)', N'{S}', @q);
SET @carga = REPLACE(@carga, N'~', N'''');

BEGIN TRY
    BEGIN TRANSACTION;
    EXEC (@carga);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'FALLO la carga: ', ERROR_MESSAGE());
    THROW;
END CATCH

-- Resultado (esperado: 36 zonas sin tipo de vehiculo y 97 poligonos).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~cuadrantes (zonas)~ AS Catalogo, COUNT(*) AS Valores FROM {S}.tblCuadrantes WHERE TipoVehiculo IS NULL UNION ALL
SELECT N~poligonos por vista~, COUNT(*) FROM {S}.tblCuadranteVistas', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);

DROP TABLE IF EXISTS #zona_ficha;
DROP TABLE IF EXISTS #poligono_ficha;
