using Backend.Models;

namespace Backend.Extensions
{
    public static class ResponseMappingExtension
    {
        public static BusLocationsResponse ToBusLocationsResponse(this IReadOnlyList<BusLocation> busLocations)
        {
            List<BusLocationItemResponse> busLocationItems = new List<BusLocationItemResponse>();
            foreach (BusLocation busLocation in busLocations)
            {
                busLocationItems.Add(
                    new BusLocationItemResponse()
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
                    }
                );
            }
            return new BusLocationsResponse() { BusLocations = busLocationItems };
        }
    }
}