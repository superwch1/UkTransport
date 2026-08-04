import 'package:frontend/core/network/response/bus_calling_point_item_response.dart';

class BusCallingPointsResponse {

  final List<BusCallingPointItemResponse> busCallingPoints;

  BusCallingPointsResponse._({required this.busCallingPoints});

  factory BusCallingPointsResponse.fromJson(Map<String, dynamic> json) {
    return BusCallingPointsResponse._(
      busCallingPoints: (json['callingPoints'] as List<dynamic>? ?? [])
        .map((item) => BusCallingPointItemResponse.fromJson(item as Map<String, dynamic>))
        .toList(),
    );
  }
}
