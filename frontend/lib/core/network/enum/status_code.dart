enum StatusCode {
  ok(200),
  accepted(202),
  noContent(204),
  badRequest(400),
  unauthorized(401),
  forbiddden(403),
  notFound(404),
  serverError(500),
  undocumented(0);

  final int value;
  const StatusCode(this.value);

  static StatusCode fromInt(int value) {
    return StatusCode.values.firstWhere(
      (e) => e.value == value,
      orElse: () => StatusCode.undocumented,
    );
  }
}