import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/domain/sesion.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../inspecciones/domain/reglas_informe.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../../vehiculos/domain/inventario.dart';
import '../providers.dart';
import 'lienzo_firma.dart';
import 'no_firma_page.dart';

/// Los dos del viaje que pueden firmar: su conductor y su inspector. No la lista de usuarios: ofrecer a
/// cualquiera como firmante lleva a elegir a quien el servidor va a rechazar, y además deja ver nombres de
/// gente ajena al viaje.
/// La transferencia que ya exista sobre este informe. Null mientras no haya firmado nadie.
final _transferenciaProvider = FutureProvider.autoDispose.family<TransferenciaVista?, String>(
  (ref, inspeccionId) => ref.watch(transferenciasRepositorioProvider).deInforme(inspeccionId),
);

final _firmantesProvider =
    FutureProvider.autoDispose.family<List<Usuario>, String>((ref, viajeId) => ref.watch(usuariosRepositorioProvider).firmantesDeViaje(viajeId));

/// Firma de la transferencia: quien entrega y quien recibe, cada uno con su trazo. Se puede firmar solo una parte;
/// la otra firma desde su dispositivo. El servidor valida que quien firma sea el usuario autenticado.
class FirmaPage extends ConsumerStatefulWidget {
  const FirmaPage({
    super.key,
    required this.vin,
    required this.inspeccionId,
    required this.proceso,
    required this.soloFirma,
    required this.viajeId,
  });

  final String vin;
  final String inspeccionId;
  final String proceso;
  final bool soloFirma;

  /// El viaje de la etapa, de donde salen los firmantes.
  final String? viajeId;

  @override
  ConsumerState<FirmaPage> createState() => _EstadoFirmaPage();
}

class _EstadoFirmaPage extends ConsumerState<FirmaPage> {
  String? _entregaId;
  String? _recibeId;
  String? _firmaEntrega;
  String? _firmaRecibe;
  bool _guardando = false;
  bool _seleccionInicial = false;

  /// Quien recibe puede firmar dejando constancia de lo que no está conforme. Entonces la transferencia cierra
  /// como `con_reservas` y el texto es obligatorio: unas reservas en blanco no dicen nada.
  bool _conReservas = false;
  final _reservas = TextEditingController();

  @override
  void dispose() {
    _reservas.dispose();
    super.dispose();
  }

  /// Quiénes son las partes. Si la transferencia ya existe **no se elige**: la primera firma las fijó y el
  /// servidor rechaza con 409 cualquier intento de cambiarlas, así que quien firma de segundo hereda. Solo
  /// cuando no hay nada se propone lo razonable: entrega quien está en sesión, recibe la otra persona.
  void _elegirPorDefecto(List<Usuario> usuarios, String? yoId, TransferenciaVista? transferencia) {
    if (transferencia != null) {
      _entregaId = transferencia.entregaId;
      _recibeId = transferencia.recibeId;
      return;
    }
    if (_seleccionInicial || usuarios.isEmpty) return;
    _seleccionInicial = true;
    _entregaId = usuarios.any((u) => u.id == yoId) ? yoId : usuarios.first.id;
    _recibeId = usuarios.where((u) => u.id != _entregaId).firstOrNull?.id;
  }

  /// El trazo que viaja: el de quien tiene la sesión. Null mientras no se pueda firmar.
  String? _miFirma(String? yoId) => firmaDeLaSesion(
        yoId: yoId,
        entregaId: _entregaId,
        recibeId: _recibeId,
        firmaEntrega: _firmaEntrega,
        firmaRecibe: _firmaRecibe,
      );

  bool _puedeFirmar(String? yoId) =>
      _miFirma(yoId) != null && !_guardando && (!_conReservas || _reservas.text.trim().isNotEmpty);

  Future<void> _registrar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final firma = _miFirma(usuarioId);
    if (firma == null || usuarioId == null) return;
    setState(() => _guardando = true);
    try {
      await ref.read(colaRepositorioProvider).agregar(FirmaJob(
            id: const Uuid().v4(),
            etiqueta: 'Firma ${widget.vin} · ${widget.proceso}',
            creadoEn: DateTime.now().millisecondsSinceEpoch,
            usuarioId: usuarioId,
            inspeccionId: widget.inspeccionId,
            entregaId: _entregaId!,
            recibeId: _recibeId!,
            firma: firma,
            reservas: _conReservas ? _reservas.text.trim() : null,
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      if (!mounted) return;
      final enCola = ref.read(syncControllerProvider).pendientes > 0;
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          title: const Text('Firma registrada'),
          content: Text(enCola ? 'Quedó en cola; se enviará cuando la señal sea estable.' : 'Cambio de responsabilidad registrado.'),
          actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
        ),
      );
      if (mounted) context.pop();
    } catch (e) {
      if (mounted) {
        await showDialog<void>(
          context: context,
          builder: (contexto) => AlertDialog(
            title: const Text('No se pudo registrar la firma'),
            content: Text(mensajeDeError(e)),
            actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _guardando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final yoId = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
    final viajeId = widget.viajeId;
    if (viajeId == null) {
      return Scaffold(
        appBar: AppBar(title: Text('Firma · ${widget.proceso}')),
        body: const EstadoMensaje(
          icono: Icons.link_off,
          titulo: 'Esta etapa no tiene viaje asociado',
          descripcion: 'Los firmantes salen del viaje, así que no hay a quién ofrecer. Avisa al administrador.',
        ),
      );
    }
    final usuarios = ref.watch(_firmantesProvider(viajeId));
    final transferencia = ref.watch(_transferenciaProvider(widget.inspeccionId));

    void recargar() {
      ref.invalidate(_firmantesProvider(viajeId));
      ref.invalidate(_transferenciaProvider(widget.inspeccionId));
    }

    return Scaffold(
      appBar: AppBar(title: Text('Firma · ${widget.proceso}')),
      body: switch ((usuarios, transferencia)) {
        (AsyncError(:final error), _) || (_, AsyncError(:final error)) => EstadoMensaje(
            icono: Icons.warning_amber_rounded,
            colorIcono: Colores.peligro,
            titulo: 'No se pudo cargar la transferencia',
            descripcion: mensajeDeError(error),
            accion: FilledButton(onPressed: recargar, child: const Text('Reintentar')),
          ),
        (AsyncData(value: final lista), AsyncData(value: final t)) => Builder(
            builder: (context) {
              _elegirPorDefecto(lista, yoId, t);
              return _formulario(context, lista, yoId, t);
            },
          ),
        _ => const EstadoCargando(),
      },
    );
  }

  Widget _formulario(BuildContext context, List<Usuario> lista, String? yoId, TransferenciaVista? transferencia) {
    final tema = Theme.of(context).textTheme;
    final distintos = _entregaId != null && _recibeId != null && _entregaId != _recibeId;
    // Solo se firma la propia: el servidor atribuye la firma al usuario del token, así que la de la otra parte
    // no viajaría. Se deja ver quién es y que su firma queda pendiente.
    final yoEntrego = yoId != null && yoId == _entregaId;
    final yoRecibo = yoId != null && yoId == _recibeId;
    return SafeArea(
      child: ListView(
        padding: const EdgeInsets.all(Espacio.lg),
        children: [
          Text('${widget.vin} · ${widget.proceso}${widget.soloFirma ? ' · firma sobre el último informe' : ''}', style: tema.bodySmall),
          const SizedBox(height: Espacio.lg),
          _Parte(
            etiqueta: 'Quien entrega',
            usuarios: lista,
            valor: _entregaId,
            nombreFijo: transferencia?.entregaNombre,
            yaFirmo: transferencia?.firmoEntrega,
            alElegir: transferencia != null ? null : (id) => setState(() => _entregaId = id),
          ),
          const SizedBox(height: Espacio.sm),
          if (yoEntrego)
            LienzoFirma(etiqueta: 'Tu firma', onCambio: (p) => setState(() => _firmaEntrega = p))
          else
            Text('Su firma se registra desde su propia sesión.', style: tema.bodySmall),
          const SizedBox(height: Espacio.lg),
          _Parte(
            etiqueta: 'Quien recibe',
            usuarios: lista,
            valor: _recibeId,
            nombreFijo: transferencia?.recibeNombre,
            yaFirmo: transferencia?.firmoRecibe,
            alElegir: transferencia != null ? null : (id) => setState(() => _recibeId = id),
          ),
          const SizedBox(height: Espacio.sm),
          if (yoRecibo)
            LienzoFirma(etiqueta: 'Tu firma', onCambio: (p) => setState(() => _firmaRecibe = p))
          else
            Text('Su firma se registra desde su propia sesión.', style: tema.bodySmall),
          // Solo quien recibe: el servidor responde 403 a cualquier otro, y además no tendría sentido que
          // quien entrega pusiera reservas sobre lo que él mismo está entregando.
          if (yoRecibo) ...[
            const SizedBox(height: Espacio.md),
            SwitchListTile(
              value: _conReservas,
              onChanged: (v) => setState(() => _conReservas = v),
              title: const Text('Recibo con reservas'),
              subtitle: Text('Firmas, y queda escrito con qué no estás conforme.', style: tema.bodySmall),
              contentPadding: EdgeInsets.zero,
            ),
            if (_conReservas)
              TextField(
                controller: _reservas,
                onChanged: (_) => setState(() {}),
                minLines: 2,
                maxLines: 4,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'Observaciones',
                  hintText: 'Qué llega distinto de lo que dice el informe',
                ),
              ),
            if (_conReservas && _reservas.text.trim().isEmpty)
              Semantics(
                liveRegion: true,
                child: Text('Escribe tus observaciones: sin ellas no es un recibo con reservas.',
                    style: tema.bodySmall?.copyWith(color: Colores.peligro)),
              ),
          ],
          const SizedBox(height: Espacio.md),
          if (!distintos)
            Semantics(
              liveRegion: true,
              child: Text('Quien entrega y quien recibe deben ser distintos.', style: tema.bodySmall?.copyWith(color: Colores.peligro)),
            )
          else if (!yoEntrego && !yoRecibo)
            Semantics(
              liveRegion: true,
              child: Text('Solo puedes firmar si eres quien entrega o quien recibe.', style: tema.bodySmall?.copyWith(color: Colores.peligro)),
            ),
          Text(
            transferencia == null
                ? 'Cada parte firma desde su sesión: la otra queda pendiente. Quien firma primero fija los dos nombres.'
                : 'Los firmantes los fijó la primera firma y ya no se cambian.',
            style: tema.bodySmall,
          ),
          const SizedBox(height: Espacio.lg),
          FilledButton(
            onPressed: _puedeFirmar(yoId) ? _registrar : null,
            child: _guardando
                ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                : Text(_conReservas ? 'Registrar recibo con reservas' : 'Registrar mi firma'),
          ),
          // La salida cuando la otra parte se niega. Solo la ve quien sí está: la constancia la levanta el
          // otro, nunca quien no firma.
          if ((yoEntrego || yoRecibo) && distintos) ...[
            const SizedBox(height: Espacio.md),
            TextButton(
              onPressed: _guardando ? null : () => _abrirNoFirma(lista, yoId, transferencia),
              child: Text('${_nombreDeLaOtraParte(lista, yoId, transferencia)} no firma'),
            ),
          ],
        ],
      ),
    );
  }

  /// Cómo se llama la otra parte. Sale de la transferencia si ya existe, y si no de los firmantes del viaje.
  String _nombreDeLaOtraParte(List<Usuario> lista, String? yoId, TransferenciaVista? transferencia) {
    final otroId = yoId == _entregaId ? _recibeId : _entregaId;
    final deLaTransferencia = yoId == _entregaId ? transferencia?.recibeNombre : transferencia?.entregaNombre;
    return deLaTransferencia ?? lista.where((u) => u.id == otroId).firstOrNull?.nombre ?? 'La otra parte';
  }

  Future<void> _abrirNoFirma(List<Usuario> lista, String? yoId, TransferenciaVista? transferencia) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => NoFirmaPage(
          vin: widget.vin,
          inspeccionId: widget.inspeccionId,
          proceso: widget.proceso,
          entregaId: _entregaId!,
          recibeId: _recibeId!,
          nombreQueNoFirma: _nombreDeLaOtraParte(lista, yoId, transferencia),
        ),
      ),
    );
    if (mounted) context.pop();
  }
}

/// Una de las dos partes. Se elige mientras la transferencia no exista; después es un nombre fijo con el
/// estado de su firma, que es lo que hay que ver cuando entre una firma y la otra pasan horas.
class _Parte extends StatelessWidget {
  const _Parte({
    required this.etiqueta,
    required this.usuarios,
    required this.valor,
    required this.nombreFijo,
    required this.yaFirmo,
    required this.alElegir,
  });

  final String etiqueta;
  final List<Usuario> usuarios;
  final String? valor;
  final String? nombreFijo;
  final bool? yaFirmo;

  /// Null = ya no se puede elegir.
  final ValueChanged<String>? alElegir;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final elegir = alElegir;
    if (elegir == null) {
      final nombre = nombreFijo ?? usuarios.where((u) => u.id == valor).firstOrNull?.nombre ?? '—';
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(etiqueta, style: tema.titleMedium?.copyWith(fontSize: 15)),
          const SizedBox(height: Espacio.xs),
          Row(
            children: [
              Expanded(child: Text(nombre, style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario))),
              if (yaFirmo == true)
                const Insignia('Ya firmó', tono: TonoInsignia.exito)
              else
                const Insignia('Falta su firma', tono: TonoInsignia.advertencia),
            ],
          ),
        ],
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(etiqueta, style: tema.titleMedium?.copyWith(fontSize: 15)),
        const SizedBox(height: Espacio.sm),
        Wrap(
          spacing: Espacio.sm,
          runSpacing: Espacio.xs,
          children: [
            for (final u in usuarios)
              Semantics(
                label: '$etiqueta: ${u.nombre}',
                child: ChoiceChip(
                  label: Text(u.nombre),
                  tooltip: etiquetar(etiquetaRol, u.rol),
                  selected: valor == u.id,
                  materialTapTargetSize: MaterialTapTargetSize.padded,
                  onSelected: (_) => elegir(u.id),
                ),
              ),
          ],
        ),
      ],
    );
  }
}
