import 'package:frontend/core/network/response/bus_location_item_response.dart';

class BusLocationsResponse {

  final List<BusLocationItemResponse> busLocations;

  BusLocationsResponse._({required this.busLocations});

  factory BusLocationsResponse.fromJson(Map<String, dynamic> json) {
    return BusLocationsResponse._(
      busLocations: (json['busLocations'] as List<dynamic>? ?? [])
        .map((item) => BusLocationItemResponse.fromJson(item as Map<String, dynamic>))
        .toList(),
    );
  }
}