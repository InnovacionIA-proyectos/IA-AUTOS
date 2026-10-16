import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import 'data/sofasa_repositorio.dart';

final sofasaRepositorioProvider = Provider<SofasaRepositorio>((ref) => SofasaRepositorio(ref.watch(clienteApiProvider)));
