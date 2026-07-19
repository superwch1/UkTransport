using Backend.Models;

namespace Backend.Extensions
{
    public static class ResponseMappingExtension
    {
        public static BusLocationItemResponse ToBusLocationItemResponse(this BusLocation busLocation)
        {
            return new BusLocationItemResponse()
            {
                Id = busLocation.Id,
                RecordedAtTime = busLocation.RecordedAtTime,
                OperatorRef = busLocation.OperatorRef,
                PublishedLineName = busLocation.PublishedLineName,
                OriginName = busLocation.OriginName,
                OriginRef = busLocation.OriginRef,
                OriginAimedDepartureTime = busLocation.OriginAimedDepartureTime,
                DestinationName = busLocation.DestinationName,
                DestinationRef = busLocation.DestinationRef,
                DestinationAimedArrivalTime = busLocation.DestinationAimedArrivalTime,
                VehicleRef = busLocation.VehicleRef,
                Latitude = busLocation.Latitude,
                Longitude = busLocation.Longitude,
                Bearing = busLocation.Bearing
            };
        }

        public static BusLocationsResponse ToBusLocationsResponse(this IReadOnlyList<BusLocation> busLocations)
        {
            List<BusLocationItemResponse> busLocationItems = new List<BusLocationItemResponse>();
            foreach (BusLocation busLocation in busLocations)
            {
                busLocationItems.Add(busLocation.ToBusLocationItemResponse());
            }
            return new BusLocationsResponse() { BusLocations = busLocationItems };
        }

        public static BusStopsResponse ToBusStopsResponse(this IReadOnlyList<BusStop> busStops)
        {
            List<BusStopItemResponse> busStopItems = new List<BusStopItemResponse>();
            foreach (BusStop busStop in busStops)
            {
                busStopItems.Add(new BusStopItemResponse()
                {
                    Id = busStop.Id,
                    CommonName = busStop.CommonName,
                    Bearing = busStop.Bearing,
                    Latitude = busStop.Latitude,
                    Longitude = busStop.Longitude,
                });
            }
            return new BusStopsResponse() { BusStops = busStopItems };
        }
    }
}