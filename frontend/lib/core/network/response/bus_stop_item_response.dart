class BusStopItemResponse {

  final String id;
  final String commonName;
  final int bearing;
  final double latitude;
  final double longitude;

  BusStopItemResponse._({
    required this.id,
    required this.commonName,
    required this.bearing,
    required this.latitude,
    required this.longitude,
  });

  factory BusStopItemResponse.fromJson(Map<String, dynamic> json) {
    return BusStopItemResponse._(
      id: json['id'] as String,
      commonName: json['commonName'] as String,
      bearing: json['bearing'] as int,
      latitude: (json['latitude'] as num).toDouble(),
      longitude: (json['longitude'] as num).toDouble(),
    );
  }
}