import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:frontend/core/constants/constants.dart';
import 'package:frontend/features/bus/view_models/bus_map_view_model.dart';
import 'package:frontend/features/bus/widgets/bus_location_symbols.dart';
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
          onMapCreated: (controller) async => await viewModel.onMapCreated(controller, context),
          onCameraIdle: () async => await viewModel.onCameraIdle(context),
          onCameraMove: (cameraPosition) async => viewModel.onCameraMove(cameraPosition),
          
          attributionButtonPosition: AttributionButtonPosition.bottomLeft, 
          styleString: Constants.mapStyleUrl,

          compassEnabled: false,
          rotateGesturesEnabled: false,  
          tiltGesturesEnabled: false,
          trackCameraPosition: true,          
        ),

        BusLocationSymbols(
          symbolsNotifier: viewModel.busSymbolsNotifier, 
          symbolSize: viewModel.symbolSize,
          onSymbolTap: (symbol) => null,
        )
      ],
    );
  }
}