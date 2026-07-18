import 'dart:math';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:frontend/core/network/enum/status_code.dart';
import 'package:frontend/core/network/transport_api_service.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

class BusMapViewModel {
  BusMapViewModel(this.transportApiService);

  final TransportApiService transportApiService;

  static const _sourceId = 'buses-source';
  static const _layerId = 'buses-layer';

  MapLibreMapController? _mapController;
  bool _layerReady = false;

  final Set<String> _registeredImages = {};
  final ValueNotifier<String?> selectedBusIdNotifier = ValueNotifier(null);

  List<dynamic> _lastBuses = [];

  Future<void> onMapCreated(MapLibreMapController controller, BuildContext context) async {
    _mapController = controller;
  }

  Future<void> onStyleLoaded() async {
    final controller = _mapController!;

    await controller.addGeoJsonSource(_sourceId, {'type': 'FeatureCollection', 'features': <dynamic>[]});

    await controller.addSymbolLayer(
      _sourceId,
      _layerId,
      SymbolLayerProperties(
        iconImage: [Expressions.get, 'image'],
        iconRotate: ['to-number', [Expressions.get, 'bearing']],

        // collision fully off, baked into the layer definition:
        iconAllowOverlap: true,
        iconIgnorePlacement: true,
        iconRotationAlignment: 'map',
        
        // when boxes stack, selected bus draws on top:
        symbolSortKey: [
          'case', ['==', [Expressions.get, 'selected'], true], 2.0, 1.0,
        ],
      ),
    );

    _layerReady = true;

    _mapController!.onFeatureTapped.add(_onFeatureTapped);
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
    if (response.statusCode != StatusCode.ok) return;

    _lastBuses = response.data?.busLocations ?? [];

    // images must exist before the source references them
    for (final bus in _lastBuses) {
      await _ensureBusImage(bus.publishedLineName);
    }

    await _emitSource();
  }

  Future<void> _emitSource() async {
    final selectedId = selectedBusIdNotifier.value;

    final features = _lastBuses
      .map<Map<String, dynamic>>((bus) => {
          'type': 'Feature',
          'id': bus.id,
          'geometry': {
            'type': 'Point',
            'coordinates': [bus.longitude, bus.latitude],
          },
          'properties': {
            'busId': bus.id,
            'image': 'bus-${bus.publishedLineName}',
            'bearing': (bus.bearing ?? 0).toDouble(),
            'selected': bus.id == selectedId,
          },
        })
      .toList();

    await _mapController!.setGeoJsonSource(_sourceId, {
      'type': 'FeatureCollection',
      'features': features,
    });
  }


  void _onFeatureTapped(Point<double> point, LatLng coordinates, String id, String layerId, Annotation? annotation,) {
    if (layerId != _layerId) return;
    final busId = id.toString();
    print(layerId);
    print(busId);

    selectedBusIdNotifier.value = selectedBusIdNotifier.value == busId ? null : busId;
    _emitSource();
  }

  Future<void> _ensureBusImage(String lineName) async {
    final imageName = 'bus-$lineName';
    if (_registeredImages.contains(imageName)) return;
    final bytes = await _renderBusBox(lineName);
    await _mapController!.addImage(imageName, bytes);
    _registeredImages.add(imageName);
  }

  Future<Uint8List> _renderBusBox(String label) async {
    const fontSize = 20.0, paddingX = 12.0, paddingY = 8.0, radius = 8.0;

    final tp = TextPainter(
      text: TextSpan(
        text: label,
        style: const TextStyle(
          color: Colors.white,
          fontSize: fontSize,
          fontWeight: FontWeight.w700,
        ),
      ),
    )..layout();

    final boxW = tp.width + paddingX * 2;
    final boxH = tp.height + paddingY * 2;

    final recorder = ui.PictureRecorder();
    final canvas = Canvas(recorder);

    canvas.drawRRect(
      RRect.fromRectAndRadius(
        Rect.fromLTWH(0, 0, boxW, boxH),
        const Radius.circular(radius),
      ),
      Paint()..color = const Color(0xFF1565C0),
    );
    tp.paint(canvas, const Offset(paddingX, paddingY));

    final image = await recorder.endRecording().toImage((boxW).ceil(), (boxH).ceil());
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    return data!.buffer.asUint8List();
  }

  void dispose() {
    _mapController?.onFeatureTapped.remove(_onFeatureTapped);
    selectedBusIdNotifier.dispose();
  }
}