import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:frontend/core/network/transport_api_service.dart';
import 'package:frontend/features/bus/pages/bus_map_page.dart';

final transportApiServiceProvider = Provider((ref) => TransportApiService());

void main() {

  final container = ProviderContainer();  

  runApp(
    UncontrolledProviderScope(
      container: container,
      child: const MyApp(),
    ),
  );  
}

class MyApp extends StatelessWidget {
  const MyApp({super.key});

  // This widget is the root of your application.
  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      home: BusMapPage()
    );
  }
}