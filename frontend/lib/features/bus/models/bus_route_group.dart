import 'package:frontend/core/network/response/bus_route_item_response.dart';

/// One search result: the routes of a single line, in a single direction, that share an end.
///
/// The backend returns one route per origin/destination pair. Within a line and direction,
/// several of those may run to the same destination from different origins — or out of the same
/// origin to different destinations — and those collapse into one result with the varying end
/// joined by a slash. Routes that share neither end stay separate, so three routes with three
/// different origins *and* three different destinations are three results, not one.
class BusRouteGroup {

  final String lineName;
  final String direction;

  /// Distinct operator names, in the order the backend returned them. Kept as a list rather
  /// than a joined string because the operator filter matches against the individual names.
  final List<String> operatorNames;

  /// Distinct origin stop names, in the order the backend returned them, joined by " / ".
  final String originNames;

  /// Distinct destination stop names, in the order the backend returned them, joined by " / ".
  final String destinationNames;

  /// The individual routes this group was built from, so a tap can drill down to one of them.
  final List<BusRouteItemResponse> busRoutes;

  BusRouteGroup._({
    required this.lineName,
    required this.direction,
    required this.operatorNames,
    required this.originNames,
    required this.destinationNames,
    required this.busRoutes,
  });

  /// Identifies the group within a result set. The ends are part of the key because one line
  /// and direction can produce several results.
  String get groupKey => '$lineName|$direction|$originNames|$destinationNames';

  String get operatorNamesLabel => operatorNames.join(' / ');

  /// True when the group is run by any of [selectedOperatorNames], used by the filter. A group
  /// can span several operators, so matching one of them is enough to keep it.
  bool isRunByAnyOperator(Set<String> selectedOperatorNames) {
    return operatorNames.any(selectedOperatorNames.contains);
  }

  /// Splits by line and direction, merges the routes that share an end within each, then sorts
  /// by line name (numerically where both line names are numbers, which is the common case) so
  /// the two directions of a line sit next to each other.
  static List<BusRouteGroup> fromBusRoutes(List<BusRouteItemResponse> busRoutes) {
    final Map<String, List<BusRouteItemResponse>> routesByLineAndDirection = {};
    for (final BusRouteItemResponse busRoute in busRoutes) {
      routesByLineAndDirection
        .putIfAbsent('${busRoute.lineName}|${busRoute.direction}', () => [])
        .add(busRoute);
    }

    final groups = <BusRouteGroup>[];
    for (final List<BusRouteItemResponse> routes in routesByLineAndDirection.values) {
      groups.addAll(_mergeSharedEnds(routes));
    }

    groups.sort((a, b) {
      final lineNameOrder = compareLineNames(a.lineName, b.lineName);
      if (lineNameOrder != 0) {
        return lineNameOrder;
      }

      final directionOrder = a.direction.toLowerCase().compareTo(b.direction.toLowerCase());
      return directionOrder != 0
        ? directionOrder
        : a.originNames.toLowerCase().compareTo(b.originNames.toLowerCase());
    });

    return groups;
  }

  /// Compares two line names, ordering numeric ones numerically so 9 comes before 86.
  static int compareLineNames(String a, String b) {
    final numberA = int.tryParse(a);
    final numberB = int.tryParse(b);

    if (numberA != null && numberB != null) {
      return numberA.compareTo(numberB);
    }
    if (numberA != null) {
      return -1;
    }
    if (numberB != null) {
      return 1;
    }

    return a.toLowerCase().compareTo(b.toLowerCase());
  }

  /// A route can share its destination with one route and its origin with another, so buckets
  /// are taken largest-first and each route is only spent once. Whatever is left over shares
  /// no end with anything and becomes a result of its own.
  static List<BusRouteGroup> _mergeSharedEnds(List<BusRouteItemResponse> routes) {
    final remainingRoutes = [...routes];
    final groups = <BusRouteGroup>[];

    while (remainingRoutes.isNotEmpty) {
      final List<BusRouteItemResponse>? sharedEndRoutes = _largestSharedEndBucket(remainingRoutes);
      if (sharedEndRoutes == null) {
        groups.addAll(remainingRoutes.map((busRoute) => _buildGroup([busRoute])));
        break;
      }

      groups.add(_buildGroup(sharedEndRoutes));
      remainingRoutes.removeWhere(sharedEndRoutes.contains);
    }

    return groups;
  }

  /// The biggest set of routes sharing an origin or a destination, or null when no two routes
  /// share either end.
  static List<BusRouteItemResponse>? _largestSharedEndBucket(List<BusRouteItemResponse> routes) {
    final Map<String, List<BusRouteItemResponse>> routesByDestination = {};
    final Map<String, List<BusRouteItemResponse>> routesByOrigin = {};

    for (final BusRouteItemResponse busRoute in routes) {
      routesByDestination.putIfAbsent(busRoute.destinationBusStopId, () => []).add(busRoute);
      routesByOrigin.putIfAbsent(busRoute.originBusStopId, () => []).add(busRoute);
    }

    // Destination buckets come first, so a tie on size keeps "several origins into one
    // destination" rather than the mirror image.
    List<BusRouteItemResponse>? largestBucket;
    for (final bucket in [...routesByDestination.values, ...routesByOrigin.values]) {
      if (bucket.length < 2) {
        continue;
      }
      if (largestBucket == null || bucket.length > largestBucket.length) {
        largestBucket = bucket;
      }
    }

    return largestBucket;
  }

  static BusRouteGroup _buildGroup(List<BusRouteItemResponse> routes) {
    return BusRouteGroup._(
      lineName: routes.first.lineName,
      direction: routes.first.direction,
      operatorNames: _distinct(routes.map((busRoute) => busRoute.operatorName)),
      originNames: _distinct(routes.map((busRoute) => busRoute.originName)).join(' / '),
      destinationNames: _distinct(routes.map((busRoute) => busRoute.destinationName)).join(' / '),
      busRoutes: routes,
    );
  }

  /// Set.add returns false for a name already seen, so this drops duplicates while keeping
  /// the original order.
  static List<String> _distinct(Iterable<String> names) {
    final seenNames = <String>{};
    return names.where(seenNames.add).toList();
  }
}
