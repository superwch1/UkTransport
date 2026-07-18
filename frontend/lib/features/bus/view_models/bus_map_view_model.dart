import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:frontend/core/network/enum/status_code.dart';
import 'package:frontend/core/network/transport_api_service.dart';
import 'package:frontend/features/bus/models/bus_location_symbol.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

class BusMapViewModel {

  final ValueNotifier<List<BusLocationSymbol>> symbolsNotifier = ValueNotifier([]);
  final double symbolSize = 45;

  final TransportApiService transportApiService;
  MapLibreMapController? _mapController;

  BusMapViewModel(this.transportApiService);

  Future onMapCreated(MapLibreMapController controller, BuildContext context) async {
    _mapController = controller;
    _mapController?.addListener(() => onCameraMove());

    if (context.mounted) {
      await showBusLocations(context);
    }
  }

  void onCameraMove() {
    symbolsNotifier.value = [];

    final cameraPosition = _mapController?.cameraPosition;
    if (cameraPosition == null) {
      return;
    }
  }

  Future onCameraIdle(BuildContext context) async {
    await showBusLocations(context);
  }

  Future showBusLocations(BuildContext context) async {

    if (_mapController == null) {
      return;
    }

    final mediaQueryData = MediaQuery.of(context);
    final dpr = mediaQueryData.devicePixelRatio;

    final bounds = await _mapController!.getVisibleRegion();
    final north = bounds.northeast.latitude;
    final south = bounds.southwest.latitude;
    final east = bounds.northeast.longitude;
    final west = bounds.southwest.longitude;

    final response = await transportApiService.getBusLocations(north, south, east, west);
    if (response.statusCode == StatusCode.ok) {

      List<BusLocationSymbol> busLocations = [];
      for(final busLocation in response.data?.busLocations ?? []) {

        final latLng = LatLng(busLocation.latitude, busLocation.longitude);
        final screenPoint = await _mapController!.toScreenLocation(latLng);
        
        final left = kIsWeb || defaultTargetPlatform == TargetPlatform.iOS
          ? screenPoint.x.toDouble() - symbolSize / 2
          : screenPoint.x.toDouble() / dpr - symbolSize / 2;

        final top = kIsWeb || defaultTargetPlatform == TargetPlatform.iOS
          ? screenPoint.y.toDouble() - symbolSize / 2
          : screenPoint.y.toDouble() / dpr - symbolSize / 2;

        busLocations.add(BusLocationSymbol(
          id: busLocation.id,
          publishedLineName: busLocation.publishedLineName,
          left: left,
          top: top,
          isHighlighted: false
        ));
      }
      symbolsNotifier.value = busLocations;

    } else {
      // Handle error
    }
  }
}