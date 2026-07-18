import 'package:frontend/core/network/enum/status_code.dart';

class ApiResponse<T> {
  StatusCode statusCode;
  String message;
  T? data;

  ApiResponse(this.statusCode, this.message, this.data);
}