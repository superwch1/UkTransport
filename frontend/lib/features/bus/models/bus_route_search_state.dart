import 'package:flutter/foundation.dart';
import 'package:frontend/features/bus/models/bus_route_group.dart';

/// Everything the search page renders from, as one immutable value.
@immutable
class BusRouteSearchState {

  /// The trimmed text the results belong to — empty means nothing has been searched yet.
  final String query;

  final bool isSearching;
  final String? errorMessage;

  /// All groups returned for [query], before the operator filter is applied.
  final List<BusRouteGroup> busRouteGroups;

  /// The distinct operators present in [busRouteGroups], sorted, for the filter bar.
  final List<String> operatorNames;

  /// Operators the user ticked. Empty means no filter, so everything shows.
  final Set<String> selectedOperatorNames;

  const BusRouteSearchState({
    required this.query,
    required this.isSearching,
    required this.errorMessage,
    required this.busRouteGroups,
    required this.operatorNames,
    required this.selectedOperatorNames,
  });

  const BusRouteSearchState.idle()
    : query = '',
      isSearching = false,
      errorMessage = null,
      busRouteGroups = const [],
      operatorNames = const [],
      selectedOperatorNames = const {};

  bool get hasQuery => query.isNotEmpty;

  /// Shown only once a search has actually finished, so the empty state does not flash
  /// while a request is still in flight.
  bool get hasNoResults => hasQuery && !isSearching && errorMessage == null && busRouteGroups.isEmpty;

  List<BusRouteGroup> get visibleBusRouteGroups {
    if (selectedOperatorNames.isEmpty) {
      return busRouteGroups;
    }

    return busRouteGroups
      .where((busRouteGroup) => busRouteGroup.isRunByAnyOperator(selectedOperatorNames))
      .toList();
  }

  BusRouteSearchState copyWith({
    String? query,
    bool? isSearching,
    String? errorMessage,
    bool clearErrorMessage = false,
    List<BusRouteGroup>? busRouteGroups,
    List<String>? operatorNames,
    Set<String>? selectedOperatorNames,
  }) {
    return BusRouteSearchState(
      query: query ?? this.query,
      isSearching: isSearching ?? this.isSearching,
      errorMessage: clearErrorMessage ? null : (errorMessage ?? this.errorMessage),
      busRouteGroups: busRouteGroups ?? this.busRouteGroups,
      operatorNames: operatorNames ?? this.operatorNames,
      selectedOperatorNames: selectedOperatorNames ?? this.selectedOperatorNames,
    );
  }
}
