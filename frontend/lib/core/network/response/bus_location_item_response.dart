class BusLocationItemResponse {

  final String id;
  final DateTime recordedAtTime;

  final String operatorRef;
  final String publishedLineName;

  final String originName;
  final String originRef;
  final String? originAimedDepartureTime;

  final String destinationName;
  final String destinationRef;
  final String? destinationAimedArrivalTime;

  final String vehicleRef;

  final double latitude;
  final double longitude;
  final double bearing;

  BusLocationItemResponse._({
    required this.id,
    required this.recordedAtTime,
    required this.operatorRef,
    required this.publishedLineName,
    required this.originName,
    required this.originRef,
    required this.originAimedDepartureTime,
    required this.destinationName,
    required this.destinationRef,
    required this.destinationAimedArrivalTime,
    required this.vehicleRef,
    required this.latitude,
    required this.longitude,
    required this.bearing,
  });

  factory BusLocationItemResponse.fromJson(Map<String, dynamic> json) {
    return BusLocationItemResponse._(
      id: json['id'] as String,
      recordedAtTime: DateTime.parse(json['recordedAtTime'] as String),
      operatorRef: json['operatorRef'] as String,
      publishedLineName: json['publishedLineName'] as String,
      originName: json['originName'] as String,
      originRef: json['originRef'] as String,
      originAimedDepartureTime: json['originAimedDepartureTime'] as String?,
      destinationName: json['destinationName'] as String,
      destinationRef: json['destinationRef'] as String,
      destinationAimedArrivalTime: json['destinationAimedArrivalTime'] as String?,
      vehicleRef: json['vehicleRef'] as String,
      latitude: (json['latitude'] as num).toDouble(),
      longitude: (json['longitude'] as num).toDouble(),
      bearing: (json['bearing'] as num).toDouble(),
    );
  }
}