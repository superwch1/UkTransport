import 'package:frontend/core/network/api_service.dart';
import 'package:frontend/core/network/enum/http_method.dart';
import 'package:frontend/core/network/response/api_response.dart';
import 'package:frontend/core/network/response/bus_location_item_response.dart';
import 'package:frontend/core/network/response/bus_locations_response.dart';
import 'package:frontend/core/network/response/bus_routes_response.dart';
import 'package:frontend/core/network/response/bus_stops_response.dart';

class TransportApiService extends ApiService {

  Future<ApiResponse<BusLocationsResponse>> getBusLocations(double north, double south, double east, double west) async { 
    return await super.sendRequest(
      HttpMethod.get, "transport/bus/locations", 
      queryParameters: {
        "north": north, "south": south, "east": east, "west": west,
      },
      fromJson: BusLocationsResponse.fromJson);
  }

  Future<ApiResponse<BusLocationItemResponse?>> getBusLocation(String tripScheduleKey) async { 
    return await super.sendRequest(
      HttpMethod.get, "transport/bus/$tripScheduleKey/location", 
      fromJson: BusLocationItemResponse.fromJson);
  }

  Future<ApiResponse<BusStopsResponse>> getBusStops(double north, double south, double east, double west) async { 
    return await super.sendRequest(
      HttpMethod.get, "transport/bus/stops", 
      queryParameters: {
        "north": north, "south": south, "east": east, "west": west,
      },
      fromJson: BusStopsResponse.fromJson);
  }

  Future<ApiResponse<BusRoutesResponse>> getBusRoute(String tripScheduleKey) async { 
    return await super.sendRequest(
      HttpMethod.get, "transport/bus/$tripScheduleKey/route", 
      fromJson: BusRoutesResponse.fromJson);
  }
}
