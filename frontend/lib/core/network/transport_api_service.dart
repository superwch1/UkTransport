import 'package:frontend/core/network/api_service.dart';
import 'package:frontend/core/network/enum/http_method.dart';
import 'package:frontend/core/network/response/api_response.dart';
import 'package:frontend/core/network/response/bus_locations_response.dart';

class TransportApiService extends ApiService {

  Future<ApiResponse<BusLocationsResponse>> getBusLocations(double north, double south, double east, double west) async { 
    return await super.sendRequest(
      HttpMethod.get, "transport/buslocations", 
      queryParameters: {
        "north": north, "south": south, "east": east, "west": west,
      },
      fromJson: BusLocationsResponse.fromJson);
  }
}
