import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:frontend/core/constants/constants.dart';
import 'package:frontend/core/network/response/bus_location_item_response.dart';
import 'package:frontend/features/bus/pages/bus_route_search_page.dart';
import 'package:frontend/features/bus/view_models/bus_map_view_model.dart';
import 'package:frontend/features/bus/widgets/bus_panel.dart';
import 'package:frontend/main.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

class BusMapPage extends ConsumerStatefulWidget {
  const BusMapPage({super.key});

  @override
  ConsumerState<BusMapPage> createState() => _BusMapPageState();
}

class _BusMapPageState extends ConsumerState<BusMapPage> {
  late final BusMapViewModel viewModel;

  @override
  void initState() {
    super.initState();
    viewModel = BusMapViewModel(ref.read(transportApiServiceProvider));
  }

  @override
  void dispose() {
    viewModel.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        MapLibreMap(
          onMapCreated: (controller) async => await viewModel.onMapCreated(controller),
          onStyleLoadedCallback: () async => await viewModel.onStyleLoaded(),
          onCameraIdle: () async => await viewModel.refreshMapSymbols(),

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

        Positioned(
          top: 12,
          right: 12,
          child: SafeArea(
            child: FloatingActionButton.small(
              heroTag: 'bus-route-search',
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute<void>(builder: (context) => const BusRouteSearchPage()),
              ),
              child: const Icon(Icons.search),
            ),
          ),
        ),

        Positioned(
          left: 0,
          right: 0,
          bottom: 0,
          child: ValueListenableBuilder<BusLocationItemResponse?>(
            valueListenable: viewModel.selectedBusLocationNotifier,
            builder: (context, busLocation, _) {
              if (busLocation == null) {
                return const SizedBox.shrink();
              }
              return BusPanel(
                bus: busLocation,
                onClose: () {
                  viewModel.selectedBusLocationNotifier.value = null;
                  viewModel.refreshMapSymbols();
                },
              );
            },
          ),
        ),
      ],
    );
  }
}