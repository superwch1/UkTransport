class BusCallingPointItemResponse {

  final int sequence;
  final String busStopId;
  final DateTime scheduledTime;
  final double latitude;
  final double longitude;
  final String name;

  BusCallingPointItemResponse._({
    required this.sequence,
    required this.busStopId,
    required this.scheduledTime,
    required this.latitude,
    required this.longitude,
    required this.name,
  });

  factory BusCallingPointItemResponse.fromJson(Map<String, dynamic> json) {
    return BusCallingPointItemResponse._(
      sequence: json['sequence'] as int,
      busStopId: json['busStopId'] as String,
      scheduledTime: DateTime.parse(json['scheduledTime'] as String),
      latitude: (json['latitude'] as num).toDouble(),
      longitude: (json['longitude'] as num).toDouble(),
      name: json['name'] as String,
    );
  }
}
