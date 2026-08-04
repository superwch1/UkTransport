import 'package:frontend/core/network/response/bus_route_item_response.dart';

class BusRoutesResponse {

  final List<BusRouteItemResponse> busRoutes;

  BusRoutesResponse._({required this.busRoutes});

  factory BusRoutesResponse.fromJson(Map<String, dynamic> json) {
    return BusRoutesResponse._(
      busRoutes: (json['busRoutes'] as List<dynamic>? ?? [])
        .map((item) => BusRouteItemResponse.fromJson(item as Map<String, dynamic>))
        .toList(),
    );
  }
}
