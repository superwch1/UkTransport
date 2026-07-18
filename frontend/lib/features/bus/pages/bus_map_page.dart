import 'package:flutter/material.dart';
import 'package:frontend/core/constants/constants.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

class BusMapPage extends StatelessWidget {

  const BusMapPage({super.key});

  @override
  Widget build(BuildContext context) {
    return MapLibreMap(
      styleString: Constants.mapStyleUrl,

      attributionButtonPosition: AttributionButtonPosition.bottomLeft,
      compassEnabled: false,
      rotateGesturesEnabled: false,  
      tiltGesturesEnabled: false,
    );
  }
}