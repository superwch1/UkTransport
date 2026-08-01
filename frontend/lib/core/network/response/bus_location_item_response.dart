class BusLocationItemResponse {

  final String journeyKey;
  final DateTime recordedAtTime;

  final String operatorName;
  final String publishedLineName;

  final String originName;
  final String originRef;
  final String? originAimedDepartureTime;

  final String destinationName;
  final String destinationRef;
  final String? destinationAimedArrivalTime;

  final int estimatedScheduleOffset;

  final double latitude;
  final double longitude;
  final double bearing;

  BusLocationItemResponse._({
    required this.journeyKey,
    required this.recordedAtTime,

    required this.operatorName,
    required this.publishedLineName,

    required this.originName,
    required this.originRef,
    required this.originAimedDepartureTime,

    required this.destinationName,
    required this.destinationRef,
    required this.destinationAimedArrivalTime,

    required this.estimatedScheduleOffset,

    required this.latitude,
    required this.longitude,
    required this.bearing,
    
  });

  factory BusLocationItemResponse.fromJson(Map<String, dynamic> json) {
    return BusLocationItemResponse._(
      journeyKey: json['journeyKey'] as String,
      recordedAtTime: DateTime.parse(json['recordedAtTime'] as String),
      operatorName: json['operatorName'] as String,
      publishedLineName: json['publishedLineName'] as String,
      originName: json['originName'] as String,
      originRef: json['originRef'] as String,
      originAimedDepartureTime: json['originAimedDepartureTime'] as String?,
      destinationName: json['destinationName'] as String,
      destinationRef: json['destinationRef'] as String,
      destinationAimedArrivalTime: json['destinationAimedArrivalTime'] as String?,
      estimatedScheduleOffset: (json['estimatedScheduleOffset'] as num).toInt(),
      latitude: (json['latitude'] as num).toDouble(),
      longitude: (json['longitude'] as num).toDouble(),
      bearing: (json['bearing'] as num).toDouble(),
    );
  }
}