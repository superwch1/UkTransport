import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:frontend/core/network/enum/http_method.dart';
import 'package:frontend/core/network/enum/status_code.dart';
import 'package:frontend/core/network/response/api_response.dart';

class ApiService {
  final Dio dio = Dio(
    BaseOptions(
      connectTimeout: const Duration(seconds: 6),
      // sendTimeout: const Duration(seconds: 6),
      receiveTimeout: const Duration(seconds: 6),
    ),
  );

  final serverDomain = kDebugMode 
    ? "http://192.168.1.7:5000/" // dev
    : kProfileMode 
      ? "http://192.168.1.7:5000/" // test
      : "http://192.168.1.7:5000/"; // prod


  ApiService() {
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          handler.next(options);
        },
      ),
    );
  }


  Future<ApiResponse<T>> sendRequest<T>(HttpMethod method, String path, {
    T Function(Map<String, dynamic>)? fromJson, Map<String, dynamic>? jsonData, FormData? formData, Map<String, dynamic>? queryParameters}) async {

    try { 
      final requestData  = jsonData ?? formData;
      final response = await switch (method) {
        HttpMethod.get    => dio.get   ("$serverDomain$path", queryParameters: queryParameters),
        HttpMethod.post   => dio.post  ("$serverDomain$path", data: requestData),
        HttpMethod.put    => dio.put   ("$serverDomain$path", data: requestData ),
        HttpMethod.delete => dio.delete("$serverDomain$path", data: requestData ),
      };

      final statusCode = StatusCode.fromInt(response.statusCode ?? 0);
      if (statusCode == StatusCode.ok){
        final responseBody = response.data["data"];
        final parsedData = (responseBody is Map<String, dynamic> && fromJson != null)
          ? fromJson(responseBody)
          : responseBody;

        return ApiResponse<T>(statusCode, response.data["message"], parsedData as T);
      }

    } catch (exception) { // catch all expcetion including type conversion exception
      final response = exception is DioException ? exception.response : null;
      if (response != null) {
        final message = response.data?["message"] is String 
          ? response.data["message"] as String 
          : "";

        final statusCode = StatusCode.fromInt(response.statusCode ?? 0);
        return ApiResponse<T>(statusCode, message, null);
      }
    }
    
    return ApiResponse<T>(StatusCode.undocumented, "", null);
  }
}