import 'package:flutter/material.dart';
import 'package:frontend/features/bus/models/bus_location_symbol.dart';

class BusLocationSymbols extends StatefulWidget {

  final ValueNotifier<List<BusLocationSymbol>> symbolsNotifier;
  final double symbolSize;
  
  final Function(BusLocationSymbol item) onSymbolTap;

  const BusLocationSymbols({
    required this.symbolsNotifier,
    required this.symbolSize,
    required this.onSymbolTap,
    super.key,
  });

  @override
  State<BusLocationSymbols> createState() => _BusLocationSymbolsState();
}

class _BusLocationSymbolsState extends State<BusLocationSymbols> {

  @override
  void dispose() {
    widget.symbolsNotifier.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<List<BusLocationSymbol>>(
      valueListenable: widget.symbolsNotifier,
      builder: (context, symbols, _) {
        return Stack(
          children: [
            for (final symbol in symbols)...[
              Positioned(
                left: symbol.left,
                top: symbol.top,
                child: MouseRegion(
                  cursor: SystemMouseCursors.click,
                  child: GestureDetector(
                    behavior: HitTestBehavior.opaque, // ensures the whole box is tappable
                    onTap: () => widget.onSymbolTap(symbol),
                    // DON'T add pan/drag handlers here otherwise cannot drag the map
                    child: Container(
                      width: widget.symbolSize,
                      height: widget.symbolSize,
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: symbol.isHighlighted
                          ? const Color.fromARGB(255, 201, 69, 13)
                          : const Color.fromARGB(255, 137, 132, 67),
                      ),
                        child: Text(symbol.publishedLineName, style: const TextStyle(color: Colors.white, fontSize: 10),
                      )
                    )
                  ),
                )
              ),
            ]
          ],
        );
      },
    );
  }
}