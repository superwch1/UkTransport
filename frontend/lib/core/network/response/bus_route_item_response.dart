class BusRouteItemResponse {

  final String routeKey;

  final String lineName;
  final String operatorName;

  final String originBusStopId;
  final String originName;

  final String destinationBusStopId;
  final String destinationName;

  final String direction;

  BusRouteItemResponse._({
    required this.routeKey,
    required this.lineName,
    required this.operatorName,
    required this.originBusStopId,
    required this.originName,
    required this.destinationBusStopId,
    required this.destinationName,
    required this.direction,
  });

  factory BusRouteItemResponse.fromJson(Map<String, dynamic> json) {
    return BusRouteItemResponse._(
      routeKey: json['routeKey'] as String,
      lineName: json['lineName'] as String,
      operatorName: json['operatorName'] as String,
      originBusStopId: json['originBusStopId'] as String,
      originName: json['originName'] as String,
      destinationBusStopId: json['destinationBusStopId'] as String,
      destinationName: json['destinationName'] as String,
      direction: json['direction'] as String,
    );
  }
}
