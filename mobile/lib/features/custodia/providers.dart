import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import 'data/usuarios_repositorio.dart';

final usuariosRepositorioProvider = Provider<UsuariosRepositorio>((ref) => UsuariosRepositorio(ref.watch(clienteApiProvider)));
