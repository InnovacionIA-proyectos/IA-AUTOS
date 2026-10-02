import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../domain/orden_sofasa.dart';

const _retardoBusqueda = Duration(milliseconds: 350);

final _ordenesProvider = FutureProvider.autoDispose.family<List<OrdenSofasa>, String>(
  (ref, vin) => ref.watch(sofasaRepositorioProvider).ordenes(vin: vin),
);

/// Órdenes de carga Sofasa (solo lectura), sincronizadas por el ETL.
class SofasaPage extends ConsumerStatefulWidget {
  const SofasaPage({super.key});

  @override
  ConsumerState<SofasaPage> createState() => _EstadoSofasaPage();
}

class _EstadoSofasaPage extends ConsumerState<SofasaPage> {
  final _busqueda = TextEditingController();
  String _vin = '';
  Timer? _temporizador;

  @override
  void dispose() {
    _temporizador?.cancel();
    _busqueda.dispose();
    super.dispose();
  }

  void _escribir(String valor) {
    _temporizador?.cancel();
    _temporizador = Timer(_retardoBusqueda, () => setState(() => _vin = valor.trim().toUpperCase()));
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final ordenes = ref.watch(_ordenesProvider(_vin));

    return Scaffold(
      body: SafeArea(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.sm, Espacio.lg, Espacio.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Órdenes Sofasa', style: tema.headlineMedium),
                  Text('Sincronizadas automáticamente desde el data center. Desliza para actualizar.', style: tema.bodySmall),
                  const SizedBox(height: Espacio.md),
                  TextField(
                    controller: _busqueda,
                    textCapitalization: TextCapitalization.characters,
                    autocorrect: false,
                    decoration: const InputDecoration(labelText: 'Buscar por VIN', hintText: 'Todos'),
                    onChanged: _escribir,
                  ),
                ],
              ),
            ),
            Expanded(
              child: ordenes.when(
                loading: () => const EstadoCargando(),
                error: (error, _) => EstadoMensaje(
                  icono: Icons.warning_amber_rounded,
                  colorIcono: Colores.peligro,
                  titulo: 'No se pudieron cargar las órdenes',
                  descripcion: mensajeDeError(error),
                  accion: FilledButton(onPressed: () => ref.invalidate(_ordenesProvider(_vin)), child: const Text('Reintentar')),
                ),
                data: (lista) => RefreshIndicator(
                  onRefresh: () => ref.refresh(_ordenesProvider(_vin).future),
                  child: lista.isEmpty
                      ? ListView(children: [
                          const SizedBox(height: Espacio.xxl),
                          EstadoMensaje(
                            icono: Icons.local_shipping_outlined,
                            titulo: _vin.isEmpty ? 'Aún no hay órdenes sincronizadas' : 'Ningún VIN coincide',
                          ),
                        ])
                      : ListView.separated(
                          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.xxl),
                          itemCount: lista.length,
                          separatorBuilder: (_, _) => const SizedBox(height: Espacio.md),
                          itemBuilder: (_, i) => _TarjetaOrden(lista[i]),
                        ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _TarjetaOrden extends StatelessWidget {
  const _TarjetaOrden(this.orden);

  final OrdenSofasa orden;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: TextoMono(orden.vin ?? '—')),
              if (orden.estado != null) Insignia(orden.estado!, tono: TonoInsignia.info),
            ],
          ),
          Text(orden.modelo ?? 'Sin modelo', style: tema.titleMedium?.copyWith(fontSize: 15)),
          CampoDetalle('Orden', orden.orden),
          CampoDetalle('Origen (CEDI)', orden.origen),
          CampoDetalle('Destino', orden.destino),
          CampoDetalle('Dirección', orden.direccion),
          CampoDetalle('Fecha cargue', formatearFecha(orden.fechaCargue)),
          CampoDetalle('Cita', formatearFecha(orden.cita)),
          CampoDetalle('Muelle', orden.muelle),
          CampoDetalle('Peso', orden.peso?.toString()),
        ],
      ),
    );
  }
}
