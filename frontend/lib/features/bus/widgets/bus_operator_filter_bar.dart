import 'package:flutter/material.dart';

/// The operator chips shown above the results.
///
/// Chips wrap onto as many lines as they need, and the block scrolls vertically once it would
/// grow past [_maxHeight] — a popular line number can come back with a long list of operators,
/// and the results still need most of the screen.
///
/// An empty [selectedOperatorNames] means "All", so the leading chip is selected whenever
/// nothing else is.
class BusOperatorFilterBar extends StatelessWidget {

  /// Roughly three rows of chips before the block starts scrolling.
  static const double _maxHeight = 132;

  final List<String> operatorNames;
  final Set<String> selectedOperatorNames;
  final ValueChanged<String> onOperatorToggled;
  final VoidCallback onFilterCleared;

  const BusOperatorFilterBar({
    super.key,
    required this.operatorNames,
    required this.selectedOperatorNames,
    required this.onOperatorToggled,
    required this.onFilterCleared,
  });

  @override
  Widget build(BuildContext context) {
    // One operator is not a choice, so the bar only earns its space from two upwards.
    if (operatorNames.length < 2) {
      return const SizedBox.shrink();
    }

    return ConstrainedBox(
      constraints: const BoxConstraints(maxHeight: _maxHeight),
      child: Scrollbar(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(12, 4, 12, 8),
          child: Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              FilterChip(
                label: const Text('All'),
                selected: selectedOperatorNames.isEmpty,
                onSelected: (_) => onFilterCleared(),
              ),
              for (final String operatorName in operatorNames)
                FilterChip(
                  label: Text(operatorName),
                  selected: selectedOperatorNames.contains(operatorName),
                  onSelected: (_) => onOperatorToggled(operatorName),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
