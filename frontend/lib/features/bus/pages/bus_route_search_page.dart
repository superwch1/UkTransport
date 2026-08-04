import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:frontend/features/bus/models/bus_route_group.dart';
import 'package:frontend/features/bus/models/bus_route_search_state.dart';
import 'package:frontend/features/bus/view_models/bus_route_search_view_model.dart';
import 'package:frontend/features/bus/widgets/bus_operator_filter_bar.dart';
import 'package:frontend/features/bus/widgets/bus_route_result_tile.dart';
import 'package:frontend/main.dart';

/// Searches bus routes by line name and pops the selected group back to the caller.
class BusRouteSearchPage extends ConsumerStatefulWidget {
  const BusRouteSearchPage({super.key});

  @override
  ConsumerState<BusRouteSearchPage> createState() => _BusRouteSearchPageState();
}

class _BusRouteSearchPageState extends ConsumerState<BusRouteSearchPage> {
  late final BusRouteSearchViewModel viewModel;

  final TextEditingController _queryController = TextEditingController();

  @override
  void initState() {
    super.initState();
    viewModel = BusRouteSearchViewModel(ref.read(transportApiServiceProvider));
  }

  @override
  void dispose() {
    _queryController.dispose();
    viewModel.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Search routes')),
      body: SafeArea(
        child: ValueListenableBuilder<BusRouteSearchState>(
          valueListenable: viewModel.stateNotifier,
          builder: (context, state, _) {
            return Column(
              children: [
                _searchField(state),
                BusOperatorFilterBar(
                  operatorNames: state.operatorNames,
                  selectedOperatorNames: state.selectedOperatorNames,
                  onOperatorToggled: viewModel.toggleOperatorName,
                  onFilterCleared: viewModel.clearOperatorFilter,
                ),
                Expanded(child: _results(state)),
              ],
            );
          },
        ),
      ),
    );
  }

  Widget _searchField(BusRouteSearchState state) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
      child: TextField(
        controller: _queryController,
        autofocus: true,
        textInputAction: TextInputAction.search,
        onChanged: viewModel.onQueryChanged,
        decoration: InputDecoration(
          hintText: 'Line name, e.g. 192',
          prefixIcon: const Icon(Icons.search),
          suffixIcon: _searchFieldSuffix(state),
          border: const OutlineInputBorder(),
          isDense: true,
        ),
      ),
    );
  }

  Widget? _searchFieldSuffix(BusRouteSearchState state) {
    if (state.isSearching) {
      return const Padding(
        padding: EdgeInsets.all(12),
        child: SizedBox(
          width: 16,
          height: 16,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      );
    }

    if (_queryController.text.isEmpty) {
      return null;
    }

    return IconButton(
      icon: const Icon(Icons.clear),
      onPressed: () {
        _queryController.clear();
        viewModel.clearQuery();
      },
    );
  }

  Widget _results(BusRouteSearchState state) {
    if (state.errorMessage != null) {
      return _message(Icons.error_outline, state.errorMessage!);
    }

    if (!state.hasQuery) {
      return _message(Icons.directions_bus_outlined, 'Enter a line name to find its routes.');
    }

    if (state.hasNoResults) {
      return _message(Icons.search_off, 'No routes found for "${state.query}".');
    }

    final List<BusRouteGroup> busRouteGroups = state.visibleBusRouteGroups;
    if (busRouteGroups.isEmpty) {
      return _message(Icons.filter_alt_off, 'No routes match the selected operators.');
    }

    return ListView.builder(
      padding: const EdgeInsets.only(top: 4, bottom: 12),
      itemCount: busRouteGroups.length,
      itemBuilder: (context, index) {
        final BusRouteGroup busRouteGroup = busRouteGroups[index];
        return BusRouteResultTile(
          key: ValueKey(busRouteGroup.groupKey),
          busRouteGroup: busRouteGroup,
          onTap: () => Navigator.of(context).pop(busRouteGroup),
        );
      },
    );
  }

  Widget _message(IconData icon, String message) {
    final ThemeData theme = Theme.of(context);

    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 40, color: theme.hintColor),
            const SizedBox(height: 12),
            Text(
              message,
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium?.copyWith(color: theme.hintColor),
            ),
          ],
        ),
      ),
    );
  }
}
