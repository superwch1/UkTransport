import 'package:frontend/core/network/response/bus_stop_item_response.dart';

class BusStopsResponse {

  final List<BusStopItemResponse> busStops;

  BusStopsResponse._({required this.busStops});

  factory BusStopsResponse.fromJson(Map<String, dynamic> json) {
    return BusStopsResponse._(
      busStops: (json['busStops'] as List<dynamic>? ?? [])
        .map((item) => BusStopItemResponse.fromJson(item as Map<String, dynamic>))
        .toList(),
    );
  }
}