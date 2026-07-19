import 'dart:math';
import 'dart:typed_data';
import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:frontend/core/network/enum/status_code.dart';
import 'package:frontend/core/network/response/bus_location_item_response.dart';
import 'package:frontend/core/network/transport_api_service.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

class BusMapViewModel {
  
  MapLibreMapController? _mapController;

  final TransportApiService transportApiService;
  final ValueNotifier<BusLocationItemResponse?> selectedBusIdNotifier = ValueNotifier<BusLocationItemResponse?>(null);

  List<BusLocationItemResponse> _busSymbols = [];
  bool _layerReady = false;

  // ---- style ids ----
  static const _busSourceId = 'bus-source';
  static const _busLayerId = 'bus-labels';
  static const _bgImage = 'bus-bg';

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
      [_busLayerId],
      null,
    );
    if (features.isEmpty) {
      return;
    }

    String? busId;
    Map<String, dynamic>? map = features.first.cast<String, dynamic>();
    final props = map?['properties'];
    if (props is Map) {
      busId = props['id'] as String?;
    }

    final busSymbol = _busSymbols.where((bus) => bus.id == busId).firstOrNull;
    if (busSymbol == null) {
      return;
    }

    _busSymbols = _busSymbols.where((bus) => bus.id == busId).toList();
    selectedBusIdNotifier.value = busSymbol;
    await _mapController!.setGeoJsonSource(_busSourceId, _buildBusFeature());
  }


  Future<void> onStyleLoaded() async {
    final controller = _mapController;
    if (controller == null) {
      return;
    }

    await controller.addGeoJsonSource(_busSourceId, _emptyCollection);

    // generate the bus location symbol image and add layer into the map
    final busSymbolData = await _generateBusLocationSymbol();
    await controller.addImage(_bgImage, busSymbolData);
    await _addBusLocationLayer(controller);

    _layerReady = true;
    await refreshBuses();
  }


  Future<void> refreshBuses() async {
    final controller = _mapController;
    if (controller == null || !_layerReady) {
      return;
    }

    // If a bus is selected, refresh only that bus's location
    final selectedBus = selectedBusIdNotifier.value;
    if (selectedBus != null) {
      final response = await transportApiService.getBusLocation(selectedBus.id);
      if (response.statusCode == StatusCode.ok && response.data != null) {
        _busSymbols = [ response.data! ];
        selectedBusIdNotifier.value = response.data!;
      } else {
        _busSymbols = [];
      }
    } 
    
    // If no bus is selected, refresh all buses in the current map bounds
    else {   
      final bounds = await controller.getVisibleRegion();
      final response = await transportApiService.getBusLocations(
        bounds.northeast.latitude,
        bounds.southwest.latitude,
        bounds.northeast.longitude,
        bounds.southwest.longitude,
      );
      if (response.statusCode == StatusCode.ok && response.data != null) {
        _busSymbols = response.data!.busLocations;
      } else {
        _busSymbols = [];
      }
    }

    await controller.setGeoJsonSource(_busSourceId, _buildBusFeature());
  }

  Map<String, dynamic> _buildBusFeature() {
    return {
      'type': 'FeatureCollection',
      'features': [
        for (final bus in _busSymbols) {
          'type': 'Feature',
          'geometry': {
            'type': 'Point',
            'coordinates': [bus.longitude, bus.latitude],
          },
          'properties': {
            'id': bus.id,
            'lineName': bus.publishedLineName,
            'bearing': bus.bearing
          },
        },
      ],
    };
  }


  Future<void> _addBusLocationLayer(MapLibreMapController controller) async {
    await controller.addSymbolLayer(
      _busSourceId,
      _busLayerId,
      SymbolLayerProperties(
        iconImage: _bgImage,
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


  void dispose() {
    _mapController?.dispose();
  }
}
