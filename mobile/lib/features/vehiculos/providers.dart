import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import 'data/vehiculos_repositorio.dart';

final vehiculosRepositorioProvider = Provider<VehiculosRepositorio>((ref) => VehiculosRepositorio(ref.watch(clienteApiProvider)));
