import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../auth/domain/sesion.dart';
import '../auth/presentation/auth_controller.dart';
import 'data/avisos_repositorio.dart';
import 'domain/aviso.dart';

final avisosRepositorioProvider = Provider<AvisosRepositorio>((ref) => AvisosRepositorio(ref.watch(clienteApiProvider)));

/// La primera página de la bandeja. Solo la piden quienes tienen `ver_alertas`: a un comercial la API le responde 403.
final avisosProvider = FutureProvider.autoDispose<PaginaAvisos>((ref) async {
  final usuario = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario));
  if (usuario == null || !usuario.puede(Capacidad.verAlertas)) return PaginaAvisos.vacia;
  return ref.watch(avisosRepositorioProvider).pagina();
});
