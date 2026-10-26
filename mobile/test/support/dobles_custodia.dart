import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/custodia/data/transferencias_repositorio.dart';
import 'package:traza_autos/features/custodia/data/usuarios_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

/// Las dos partes del viaje y una tercera persona, que es la única que puede ser testigo.
const ana = Usuario(id: 'u1', nombre: 'Ana Pérez', email: '', rol: 'inspector');
const luis = Usuario(id: 'u2', nombre: 'Luis Gómez', email: '', rol: 'conductor');
const marta = Usuario(id: 'u3', nombre: 'Marta Ríos', email: '', rol: 'coordinador_calidad');

class UsuariosRepositorioFalso implements UsuariosRepositorio {
  final List<String> viajesConsultados = [];
  bool listaCompletaPedida = false;

  @override
  Future<List<Usuario>> listar() async {
    listaCompletaPedida = true;
    return const [ana, luis, marta];
  }

  @override
  Future<List<Usuario>> firmantesDeViaje(String viajeId) async {
    viajesConsultados.add(viajeId);
    return const [ana, luis];
  }
}

class TransferenciasRepositorioFalso implements TransferenciasRepositorio {
  TransferenciaVista? transferencia;

  @override
  Future<TransferenciaVista?> deInforme(String inspeccionId) async => transferencia;
}

/// Como la sirve la API cuando ya firmó quien entrega y falta quien recibe.
TransferenciaVista mediaFirmada() => TransferenciaVista(
      entregaId: luis.id,
      recibeId: ana.id,
      entregaNombre: luis.nombre,
      recibeNombre: ana.nombre,
      entregaFirmoEn: DateTime(2026, 10, 26, 9),
    );
