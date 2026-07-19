import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:frontend/core/constants/constants.dart';
import 'package:frontend/features/bus/view_models/bus_map_view_model.dart';
import 'package:frontend/main.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

class BusMapPage extends ConsumerWidget {

  const BusMapPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    print("Build");
    final BusMapViewModel viewModel = BusMapViewModel(ref.read(transportApiServiceProvider));
    return Stack(
      children: [

        MapLibreMap(
          onMapCreated: (controller) async => await viewModel.onMapCreated(controller),
          onStyleLoadedCallback: () async => await viewModel.onStyleLoaded(),
          onCameraIdle: () async => await viewModel.onCameraIdle(),

           initialCameraPosition: const CameraPosition(
              target: LatLng(53.4808, -2.2426), // Manchester city centre
              zoom: 12,
            ),
          
          attributionButtonPosition: AttributionButtonPosition.bottomLeft, 
          styleString: Constants.mapStyleUrl,

          compassEnabled: false,
          rotateGesturesEnabled: false,  
          tiltGesturesEnabled: false,
          trackCameraPosition: true,          
        ),
      ],
    );
  }
}