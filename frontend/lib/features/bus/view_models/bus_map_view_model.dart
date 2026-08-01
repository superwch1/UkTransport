import 'dart:math';
import 'dart:typed_data';
import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:frontend/core/network/enum/status_code.dart';
import 'package:frontend/core/network/response/bus_location_item_response.dart';
import 'package:frontend/core/network/response/bus_route_item_response.dart';
import 'package:frontend/core/network/response/bus_stop_item_response.dart';
import 'package:frontend/core/network/transport_api_service.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

class BusMapViewModel {
  
  MapLibreMapController? _mapController;

  final TransportApiService transportApiService;
  final ValueNotifier<BusLocationItemResponse?> selectedBusLocationNotifier = ValueNotifier<BusLocationItemResponse?>(null);

  List<BusLocationItemResponse> _busLocations = [];
  List<BusStopItemResponse> _busStops = [];
  List<BusRouteItemResponse> _busRoutes = [];

  bool _layerReady = false;

  // ---- style ids ----
  static const _busLocationSourceId = 'bus-location-source';
  static const _busLocationLayerId = 'bus-location-layer';
  static const _busLocationSymbolBackground = 'bus-location-symbol-background';

  static const _busStopSourceId = 'bus-stop-source';
  static const _busStopLayerId = 'bus-stop-layer';
  static const _busStopSymbolBackground = 'bus-stop-symbol-background';

  static const _busRouteSourceId = 'bus-route-source';
  static const _busRouteLayerId = 'bus-route-layer';
  static const _busRouteSymbolBackground = 'bus-route-symbol-background';

  // ---- route line (added) ----
  static const _busRouteLineSourceId = 'bus-route-line-source';
  static const _busRouteLineLayerId = 'bus-route-line-layer';
  static const _busRouteLineColor = '#5DA9E9'; // light blue
  static const _busRouteLineWidth = 4.0;

  // bus route symbol geometry
  static const _routeRadius = 12.0;
  static const _routeBorderWidth = 1.5;


  // bus stop symbol geometry
  static const _stopRadius = 7.0;
  static const _stopBorderWidth = 1.5;
  static const _stopPointerWidth = 8.0;   // base width of the direction wedge
  static const _stopPointerHeight = 6.0;  // how far it sticks out past the circle
  static const _stopFillColor = Colors.grey; // blue; change as you like
  

  // bus location symbol geometry
  static const _textColor = '#000000';
  static const _boxColor = Colors.white;
  static const _borderWidth = 1.0;
  static const _fontSize = 14.0;
  static const _textPaddingX = 6.0;
  static const _textPaddingY = 2.0;
  static const _pointerWidth = 7.0; 
  static const _pointerHeight = 5.0; 
  static const _textFont = ['noto_sans_regular']; // https://tiles.versatiles.org/assets/glyphs/index.json

  static const Map<String, dynamic> _emptyCollection = {
    'type': 'FeatureCollection',
    'features': <dynamic>[],
  };

  BusMapViewModel(this.transportApiService);


  Future<void> onMapCreated(MapLibreMapController controller) async {
    _mapController = controller;
    controller.onFeatureTapped.add(_onFeatureTapped);
  }


  Future<void> _onFeatureTapped(Point<double> point, LatLng coordinates,
    String layerId, String featureId, Annotation? annotation) async {
    final features = await _mapController!.queryRenderedFeaturesInRect(
      Rect.fromCenter(center: Offset(point.x, point.y), width: 44, height: 44),
      [_busLocationLayerId],
      null,
    );

    if (features.isEmpty) {
      return;
    }
    
    Map<String, dynamic>? map = features.first.cast<String, dynamic>();
    final props = map?['properties'];
    if (props is! Map) {
      return;
    }

    String? journeyKey = props['journeyKey'] as String?;
    final source = map?['source'];   
    if (source == _busLocationSourceId) {
      final busLocation = _busLocations.where((bus) => bus.journeyKey == journeyKey).firstOrNull;
      if (busLocation == null) {
        return;
      }

      _busLocations = _busLocations.where((bus) => bus.journeyKey == journeyKey).toList();
      selectedBusLocationNotifier.value = busLocation;
      _busStops = [];

      final response = await transportApiService.getBusRoute(busLocation.journeyKey);
      if (response.statusCode == StatusCode.ok && response.data != null) {
        _busRoutes = response.data!.busRoutes;
      } 
      
    }
    else if (source == _busStopSourceId){
      //TODO
    }

    
    await _mapController!.setGeoJsonSource(_busLocationSourceId, _buildBusLocationFeature());
    await _mapController!.setGeoJsonSource(_busStopSourceId, _buildBusStopFeature());
    await _mapController!.setGeoJsonSource(_busRouteSourceId, _buildBusRouteFeature());
    await _mapController!.setGeoJsonSource(_busRouteLineSourceId, _buildBusRouteLineFeature());
  }


  Future<void> onStyleLoaded() async {
    final controller = _mapController;
    if (controller == null) {
      return;
    }

    await controller.addGeoJsonSource(_busLocationSourceId, _emptyCollection);
    await controller.addGeoJsonSource(_busStopSourceId, _emptyCollection);
    await controller.addGeoJsonSource(_busRouteSourceId, _emptyCollection);
    await controller.addGeoJsonSource(_busRouteLineSourceId, _emptyCollection);

    // route line added first so it renders UNDER the route/stop/location symbols
    await _addBusRouteLineLayer(controller);

    // generate the bus location symbol image and add layer into the map
    final busLocationSymbolData = await _generateBusLocationSymbol();
    await controller.addImage(_busLocationSymbolBackground, busLocationSymbolData);

    final busStopSymbolData = await _generateBusStopSymbol();
    await controller.addImage(_busStopSymbolBackground, busStopSymbolData);

    final busRouteSymbolData = await _generateBusRouteSymbol();
    await controller.addImage(_busRouteSymbolBackground, busRouteSymbolData);

    await _addBusLocationLayer(controller);
    await _addBusStopLayer(controller);
    await _addBusRouteLayer(controller);

    _layerReady = true;
    await refreshMapSymbols();
  }


  Future<void> refreshMapSymbols() async {
    final controller = _mapController;
    if (controller == null || !_layerReady) {
      return;
    }

    // If a bus is selected, refresh only that bus's location
    final selectedBus = selectedBusLocationNotifier.value;
    if (selectedBus != null) {
      final response = await transportApiService.getBusLocation(selectedBus.journeyKey);
      if (response.statusCode == StatusCode.ok && response.data != null) {
        _busLocations = [ response.data! ];
        selectedBusLocationNotifier.value = response.data!;
      } else {
        _busLocations = [];
      }
    } 
    
    // If no bus is selected, refresh all buses in the current map bounds
    else {   
      final bounds = await controller.getVisibleRegion();
      final busLocationsResponse = await transportApiService.getBusLocations(
        bounds.northeast.latitude,
        bounds.southwest.latitude,
        bounds.northeast.longitude,
        bounds.southwest.longitude,
      );
      if (busLocationsResponse.statusCode == StatusCode.ok && busLocationsResponse.data != null) {
        _busLocations = busLocationsResponse.data!.busLocations;
      } else {
        _busLocations = [];
      }

      final busStopsResponse = await transportApiService.getBusStops(
        bounds.northeast.latitude,
        bounds.southwest.latitude,
        bounds.northeast.longitude,
        bounds.southwest.longitude,
      );
      if (busStopsResponse.statusCode == StatusCode.ok && busStopsResponse.data != null) {
        _busStops = busStopsResponse.data!.busStops;
      } else {
        _busStops = [];
      } 

      _busRoutes = [];
    }

    await controller.setGeoJsonSource(_busLocationSourceId, _buildBusLocationFeature());
    await controller.setGeoJsonSource(_busStopSourceId, _buildBusStopFeature());
    await controller.setGeoJsonSource(_busRouteSourceId, _buildBusRouteFeature());
    await controller.setGeoJsonSource(_busRouteLineSourceId, _buildBusRouteLineFeature());
  }

  Map<String, dynamic> _buildBusRouteFeature() {
    return {
      'type': 'FeatureCollection',
      'features': [
        for (final busRoute in _busRoutes) {
          'type': 'Feature',
          'geometry': {
            'type': 'Point',
            'coordinates': [busRoute.longitude, busRoute.latitude],
          },
          'properties': {
            'sequence': busRoute.sequence
          },
        },
      ],
    };
  }

  // Builds a single LineString connecting the route points in sequence order.
  Map<String, dynamic> _buildBusRouteLineFeature() {
    final ordered = [..._busRoutes]..sort((a, b) => a.sequence.compareTo(b.sequence));

    return {
      'type': 'FeatureCollection',
      'features': [
        if (ordered.length >= 2)
          {
            'type': 'Feature',
            'geometry': {
              'type': 'LineString',
              'coordinates': [
                for (final busRoute in ordered) [busRoute.longitude, busRoute.latitude],
              ],
            },
            'properties': <String, dynamic>{},
          },
      ],
    };
  }

  Map<String, dynamic> _buildBusLocationFeature() {
    return {
      'type': 'FeatureCollection',
      'features': [
        for (final busLocation in _busLocations) {
          'type': 'Feature',
          'geometry': {
            'type': 'Point',
            'coordinates': [busLocation.longitude, busLocation.latitude],
          },
          'properties': {
            'journeyKey': busLocation.journeyKey,
            'lineName': busLocation.publishedLineName,
            'bearing': busLocation.bearing
          },
        },
      ],
    };
  }

  Map<String, dynamic> _buildBusStopFeature() {
    return {
      'type': 'FeatureCollection',
      'features': [
        for (final busStop in _busStops) {
          'type': 'Feature',
          'geometry': {
            'type': 'Point',
            'coordinates': [busStop.longitude, busStop.latitude],
          },
          'properties': {
            'id': busStop.id,
            'bearing': busStop.bearing
          },
        },
      ],
    };
  }


  Future<void> _addBusRouteLineLayer(MapLibreMapController controller) async {
    await controller.addLineLayer(
      _busRouteLineSourceId,
      _busRouteLineLayerId,
      const LineLayerProperties(
        lineColor: _busRouteLineColor,
        lineWidth: _busRouteLineWidth,
        lineCap: 'round',
        lineJoin: 'round',
        lineOpacity: 0.9,
      ),
    );
  }


  Future<void> _addBusRouteLayer(MapLibreMapController controller) async {
    await controller.addSymbolLayer(
      _busRouteSourceId,
      _busRouteLayerId,
      SymbolLayerProperties(
        iconImage: _busRouteSymbolBackground,
        iconSize: 1.0,
        iconRotationAlignment: 'map',

        // map-rendered text
        textField: ['get', 'sequence'],
        textFont: _textFont,
        textSize: _fontSize,
        textColor: _textColor,
        textRotationAlignment: 'map',

        iconAllowOverlap: true,
        textAllowOverlap: true,
      ),
    );
  }


  Future<void> _addBusLocationLayer(MapLibreMapController controller) async {
    await controller.addSymbolLayer(
      _busLocationSourceId,
      _busLocationLayerId,
      SymbolLayerProperties(
        iconImage: _busLocationSymbolBackground,
        iconSize: 1.0,
        iconRotate: ['+', ['get', 'bearing'], 90],
        iconRotationAlignment: 'map',

        // [top, right, bottom, left] — left reserves room for the pointer
        iconTextFitPadding: [
          _textPaddingY,
          _textPaddingX,
          _textPaddingY,
          _pointerHeight + _textPaddingX,
        ],

        // map-rendered text
        textField: ['get', 'lineName'],
        textFont: _textFont,
        textSize: _fontSize,
        textColor: _textColor,
        textRotate: ['+', ['get', 'bearing'], 90],
        textRotationAlignment: 'map',

        iconAllowOverlap: true,
        textAllowOverlap: true,
      ),
    );
  }


  Future<void> _addBusStopLayer(MapLibreMapController controller) async {
    await controller.addSymbolLayer(
      _busStopSourceId,
      _busStopLayerId,
      SymbolLayerProperties(
        iconImage: _busStopSymbolBackground,
        iconSize: 1.0,
        iconRotate: ['get', 'bearing'],
        iconRotationAlignment: 'map',

        iconAllowOverlap: true,
        textAllowOverlap: true,
      ),
    );
  }


  static Future<Uint8List> _generateBusLocationSymbol() async {
    final nominal = TextPainter(
      textDirection: TextDirection.ltr,
      text: const TextSpan(
        text: '000',
        style: TextStyle(fontSize: _fontSize, fontWeight: FontWeight.w600),
      ),
    )..layout();

    final boxWidth = nominal.width + _textPaddingX * 2;
    final boxHeight = nominal.height + _textPaddingY * 2;

    // reserve room for the border so it isn't clipped at the image edges
    final imageWidth = boxWidth + _pointerHeight + _borderWidth;
    final imageHeight = boxHeight + _borderWidth;
    final left = _pointerHeight + _borderWidth / 2; // box starts after the triangle
    final top = _borderWidth / 2;

    final boxPath = Path()..addRect(Rect.fromLTWH(left, top, boxWidth, boxHeight));

    final cy = top + boxHeight / 2;
    final pointerPath = Path()
      ..moveTo(left + 1, cy - _pointerWidth / 2)
      ..lineTo(left - _pointerHeight, cy)
      ..lineTo(left + 1, cy + _pointerWidth / 2)
      ..close();

    final shape = Path.combine(PathOperation.union, boxPath, pointerPath);

    final recorder = PictureRecorder();
    final canvas = Canvas(recorder);

    // 1. whole shape black -> colours the pointer
    canvas.drawPath(shape, Paint()..color = Colors.black);

    // 2. box fill on top, leaving the pointer black
    canvas.drawPath(boxPath, Paint()..color = _boxColor);

    // 3. border — stroke the OUTER shape so the pointer + box share one outline
    canvas.drawPath(shape,
      Paint()
        ..color = Colors.black
        ..style = PaintingStyle.stroke
        ..strokeWidth = _borderWidth
        ..strokeJoin = StrokeJoin.round,
    );

    final image = await recorder.endRecording().toImage(imageWidth.ceil(), imageHeight.ceil());
    final data = await image.toByteData(format: ImageByteFormat.png);
    return data!.buffer.asUint8List();
  }


  static Future<Uint8List> _generateBusStopSymbol() async {
    // Pad every side by the pointer length + border so the circle lands
    // dead-centre — MapLibre anchors the icon at the image centre, so the
    // circle stays on the coordinate while rotation spins the pointer.
    final pad = _stopPointerHeight + _stopBorderWidth;
    final size = _stopRadius * 2 + pad * 2;
    final center = Offset(size / 2, size / 2);

    final circlePath = Path()
      ..addOval(Rect.fromCircle(center: center, radius: _stopRadius));

    // Pointer at the top (north) so iconRotate: ['get','bearing'] aims it correctly.
    final tipY = center.dy - _stopRadius - _stopPointerHeight;
    final baseY = center.dy - _stopRadius + 0.5; // overlap slightly into the circle
    final pointerPath = Path()
      ..moveTo(center.dx - _stopPointerWidth / 2, baseY)
      ..lineTo(center.dx, tipY)
      ..lineTo(center.dx + _stopPointerWidth / 2, baseY)
      ..close();

    final shape = Path.combine(PathOperation.union, circlePath, pointerPath);

    final recorder = PictureRecorder();
    final canvas = Canvas(recorder);

    // fill (circle + pointer as one shape)
    canvas.drawPath(shape, Paint()..color = _stopFillColor);

    // shared outline around the whole shape
    canvas.drawPath(
      shape,
      Paint()
        ..color = Colors.black
        ..style = PaintingStyle.stroke
        ..strokeWidth = _stopBorderWidth
        ..strokeJoin = StrokeJoin.round,
    );

    final image = await recorder.endRecording().toImage(size.ceil(), size.ceil());
    final data = await image.toByteData(format: ImageByteFormat.png);
    return data!.buffer.asUint8List();
  }


  static Future<Uint8List> _generateBusRouteSymbol() async {
    final pad = _routeBorderWidth;
    final size = _routeRadius * 2 + pad * 2;
    final center = Offset(size / 2, size / 2);

    final circlePath = Path()
      ..addOval(Rect.fromCircle(center: center, radius: _routeRadius));

    final recorder = PictureRecorder();
    final canvas = Canvas(recorder);

    // white fill
    canvas.drawPath(circlePath, Paint()..color = Colors.white);

    // black outline
    canvas.drawPath(
      circlePath,
      Paint()
        ..color = Colors.black
        ..style = PaintingStyle.stroke
        ..strokeWidth = _routeBorderWidth,
    );

    final image = await recorder.endRecording().toImage(size.ceil(), size.ceil());
    final data = await image.toByteData(format: ImageByteFormat.png);
    return data!.buffer.asUint8List();
  }


  void dispose() {
    _mapController?.dispose();
  }
}