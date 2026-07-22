class BusRouteItemResponse {

  final int sequence;
  final String busStopId;
  final DateTime scheduledTime;
  final double latitude;
  final double longitude;

  BusRouteItemResponse._({
    required this.sequence,
    required this.busStopId,
    required this.scheduledTime,
    required this.latitude,
    required this.longitude,
  });

  factory BusRouteItemResponse.fromJson(Map<String, dynamic> json) {
    return BusRouteItemResponse._(
      sequence: json['sequence'] as int,
      busStopId: json['busStopId'] as String,
      scheduledTime: DateTime.parse('1970-01-01T${json['scheduledTime']}'),
      latitude: (json['latitude'] as num).toDouble(),
      longitude: (json['longitude'] as num).toDouble(),
    );
  }
}