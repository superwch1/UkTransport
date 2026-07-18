class BusLocationSymbol {
  final String id;
  final double left;
  final double top;
  final String publishedLineName;
  bool isHighlighted;

  BusLocationSymbol({required this.id, required this.left, required this.top, required this.publishedLineName, required this.isHighlighted});
}