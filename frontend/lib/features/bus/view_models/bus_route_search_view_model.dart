import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:frontend/core/network/enum/status_code.dart';
import 'package:frontend/core/network/response/bus_routes_response.dart';
import 'package:frontend/core/network/transport_api_service.dart';
import 'package:frontend/features/bus/models/bus_route_group.dart';
import 'package:frontend/features/bus/models/bus_route_search_state.dart';

class BusRouteSearchViewModel {

  /// Long enough that a burst of keystrokes costs one request, short enough that the list
  /// still feels like it is following along as the user types.
  static const Duration _debounceDuration = Duration(milliseconds: 300);

  final TransportApiService transportApiService;

  final ValueNotifier<BusRouteSearchState> stateNotifier =
    ValueNotifier<BusRouteSearchState>(const BusRouteSearchState.idle());

  Timer? _debounceTimer;

  /// Incremented for every search started or cancelled. A response whose id no longer matches
  /// belongs to a query the user has already typed past, so it is dropped — otherwise a slow
  /// response for "8" could land after a fast one for "86" and overwrite it.
  int _requestId = 0;

  BusRouteSearchViewModel(this.transportApiService);

  BusRouteSearchState get state => stateNotifier.value;

  /// Called on every keystroke, including deletions.
  void onQueryChanged(String query) {
    _debounceTimer?.cancel();

    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      _requestId++;
      stateNotifier.value = const BusRouteSearchState.idle();
      return;
    }

    if (trimmedQuery == state.query && !state.isSearching && state.errorMessage == null) {
      return;
    }

    // Show the spinner straight away, but keep the previous results underneath so the list
    // does not blank out between keystrokes.
    stateNotifier.value = state.copyWith(isSearching: true, clearErrorMessage: true);
    _debounceTimer = Timer(_debounceDuration, () => _search(trimmedQuery));
  }

  void toggleOperatorName(String operatorName) {
    final selectedOperatorNames = Set<String>.from(state.selectedOperatorNames);
    if (!selectedOperatorNames.remove(operatorName)) {
      selectedOperatorNames.add(operatorName);
    }

    stateNotifier.value = state.copyWith(selectedOperatorNames: selectedOperatorNames);
  }

  void clearOperatorFilter() {
    if (state.selectedOperatorNames.isEmpty) {
      return;
    }

    stateNotifier.value = state.copyWith(selectedOperatorNames: const {});
  }

  void clearQuery() {
    _debounceTimer?.cancel();
    _requestId++;
    stateNotifier.value = const BusRouteSearchState.idle();
  }

  Future<void> _search(String lineName) async {
    final requestId = ++_requestId;

    final response = await transportApiService.getBusRoutesByLineName(lineName);
    if (requestId != _requestId) {
      return;
    }

    if (response.statusCode != StatusCode.ok || response.data == null) {
      stateNotifier.value = state.copyWith(
        query: lineName,
        isSearching: false,
        errorMessage: response.message.isNotEmpty ? response.message : 'Could not search routes.',
        busRouteGroups: const [],
        operatorNames: const [],
        selectedOperatorNames: const {},
      );
      return;
    }

    final BusRoutesResponse busRoutesResponse = response.data!;
    final busRouteGroups = BusRouteGroup.fromBusRoutes(busRoutesResponse.busRoutes);

    final operatorNames = busRouteGroups
      .expand((busRouteGroup) => busRouteGroup.operatorNames)
      .toSet()
      .toList()
      ..sort((a, b) => a.toLowerCase().compareTo(b.toLowerCase()));

    // Keep the filter across keystrokes, but drop operators that are no longer in the results
    // so the chips and the selection cannot disagree.
    final selectedOperatorNames = state.selectedOperatorNames.intersection(operatorNames.toSet());

    stateNotifier.value = BusRouteSearchState(
      query: lineName,
      isSearching: false,
      errorMessage: null,
      busRouteGroups: busRouteGroups,
      operatorNames: operatorNames,
      selectedOperatorNames: selectedOperatorNames,
    );
  }

  void dispose() {
    _debounceTimer?.cancel();
    stateNotifier.dispose();
  }
}
