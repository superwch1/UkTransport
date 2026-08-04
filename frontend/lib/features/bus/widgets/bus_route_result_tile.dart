import 'package:flutter/material.dart';
import 'package:frontend/features/bus/models/bus_route_group.dart';

/// One search result: a line badge, the operator, and the combined origins and destinations.
class BusRouteResultTile extends StatelessWidget {

  final BusRouteGroup busRouteGroup;
  final VoidCallback? onTap;

  const BusRouteResultTile({super.key, required this.busRouteGroup, this.onTap});

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final String footnote = _footnote();

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _lineBadge(theme),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      busRouteGroup.operatorNamesLabel,
                      style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w600),
                    ),
                    const SizedBox(height: 6),
                    _stopRow(theme, Icons.trip_origin, busRouteGroup.originNames),
                    const SizedBox(height: 2),
                    _stopRow(theme, Icons.place, busRouteGroup.destinationNames),
                    if (footnote.isNotEmpty) ...[
                      const SizedBox(height: 6),
                      Text(
                        footnote,
                        style: theme.textTheme.bodySmall?.copyWith(color: theme.hintColor),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  /// Direction, plus how many origin/destination pairs were merged into this result.
  String _footnote() {
    final String direction = busRouteGroup.direction.isEmpty
      ? ''
      : '${busRouteGroup.direction[0].toUpperCase()}${busRouteGroup.direction.substring(1)}';

    final int routeCount = busRouteGroup.busRoutes.length;
    if (routeCount <= 1) {
      return direction;
    }

    return direction.isEmpty ? '$routeCount routes' : '$direction · $routeCount routes';
  }

  Widget _lineBadge(ThemeData theme) {
    return Container(
      constraints: const BoxConstraints(minWidth: 48),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
      decoration: BoxDecoration(
        color: theme.colorScheme.primaryContainer,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(
        busRouteGroup.lineName,
        textAlign: TextAlign.center,
        style: theme.textTheme.titleMedium?.copyWith(
          color: theme.colorScheme.onPrimaryContainer,
          fontWeight: FontWeight.w700,
        ),
      ),
    );
  }

  Widget _stopRow(ThemeData theme, IconData icon, String stopNames) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(top: 2),
          child: Icon(icon, size: 14, color: theme.hintColor),
        ),
        const SizedBox(width: 6),
        Expanded(
          child: Text(stopNames, style: theme.textTheme.bodyMedium),
        ),
      ],
    );
  }
}
