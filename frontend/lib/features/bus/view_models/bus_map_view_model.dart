import 'dart:typed_data';
import 'dart:ui' as ui;
import 'package:flutter/material.dart';
import 'package:frontend/core/network/enum/status_code.dart';
import 'package:frontend/core/network/response/bus_location_item_response.dart';
import 'package:frontend/core/network/transport_api_service.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

class BusMapViewModel {
  
  final TransportApiService transportApiService;

  MapLibreMapController? _mapController;

  final ValueNotifier<String?> selectedBusIdNotifier = ValueNotifier(null);

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
    await _refreshBuses();
  }

  Future<void> onCameraIdle() async => _refreshBuses();

  Future<void> _refreshBuses() async {
    final controller = _mapController;
    if (controller == null || !_layerReady) {
      return;
    }

    final bounds = await controller.getVisibleRegion();
    final response = await transportApiService.getBusLocations(
      bounds.northeast.latitude,
      bounds.southwest.latitude,
      bounds.northeast.longitude,
      bounds.southwest.longitude,
    );

    if (response.statusCode != StatusCode.ok) {
      return;
    }

    _busSymbols = response.data?.busLocations ?? [];
    await controller.setGeoJsonSource(_busSourceId, _buildFeatureCollection());
  }

  Map<String, dynamic> _buildFeatureCollection() {
    final selectedId = selectedBusIdNotifier.value;
    return {
      'type': 'FeatureCollection',
      'features': [
        for (final bus in _busSymbols)
          {
            'type': 'Feature',
            'geometry': {
              'type': 'Point',
              'coordinates': [bus.longitude, bus.latitude],
            },
            'properties': {
              'busId': bus.id,
              'lineName': bus.publishedLineName,
              'bearing': bus.bearing,
              'selected': bus.id == selectedId,
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

        // selected bus drawn on top (higher key = on top when overlap is on)
        symbolSortKey: ['case', ['get', 'selected'], 1, 0],
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
    final triPath = Path()
      ..moveTo(left + 1, cy - _pointerWidth / 2)
      ..lineTo(left - _pointerHeight, cy)
      ..lineTo(left + 1, cy + _pointerWidth / 2)
      ..close();

    final shape = Path.combine(PathOperation.union, boxPath, triPath);

    final recorder = ui.PictureRecorder();
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
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    return data!.buffer.asUint8List();
  }

  void dispose() {
    selectedBusIdNotifier.dispose();
  }
}
