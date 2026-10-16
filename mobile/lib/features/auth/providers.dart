import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import 'data/almacen_sesion_seguro.dart';
import 'data/auth_repositorio_api.dart';
import 'domain/almacen_sesion.dart';
import 'domain/sesion.dart';

final almacenSesionProvider = Provider<AlmacenSesion>((ref) => AlmacenSesionSeguro());

final authRepositorioProvider = Provider<AuthRepositorio>((ref) => AuthRepositorioApi(ref.watch(clienteApiProvider)));
