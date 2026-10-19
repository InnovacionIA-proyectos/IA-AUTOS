/// Un punto marcado sobre el diagrama del vehículo, con coordenadas normalizadas (0..1).
class CoordsDiagrama {
  const CoordsDiagrama(this.x, this.y);

  factory CoordsDiagrama.fromJson(Map<String, dynamic> json) =>
      CoordsDiagrama((json['x'] as num).toDouble(), (json['y'] as num).toDouble());

  final double x;
  final double y;

  Map<String, dynamic> toJson() => {'x': x, 'y': y};

  @override
  bool operator ==(Object other) => other is CoordsDiagrama && other.x == x && other.y == y;

  @override
  int get hashCode => Object.hash(x, y);
}
