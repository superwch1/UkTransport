using Backend.Models;
using System.Collections.Immutable;

namespace Backend.Extensions
{
    public static class ResponseMappingExtension
    {
        public static BusStopsResponse ToBusStopsResponse(this IReadOnlyList<Stop> busStops)
        {
            List<BusStopItemResponse> items = new List<BusStopItemResponse>();
            foreach (Stop busStop in busStops)
            {
                items.Add(new BusStopItemResponse()
                {
                    Id = busStop.Id,
                    CommonName = busStop.Name,
                    Bearing = busStop.Bearing,
                    Latitude = busStop.Latitude,
                    Longitude = busStop.Longitude,
                });
            }
            return new BusStopsResponse() { BusStops = items };
        }


        public static BusRoutesResponse ToBusRoutesResponse(this IReadOnlyList<BusRoute> busRoutes)
        {
            List<BusRouteItemResponse> items = new List<BusRouteItemResponse>();
            foreach (BusRoute busRoute in busRoutes)
            {
                items.Add(new BusRouteItemResponse()
                {
                    LineName = busRoute.LineName,
                    OperatorName = busRoute.OperatorName,
                    OriginBusStopId = busRoute.OriginBusStopId,
                    OriginName = busRoute.OriginName,
                    DestinationBusStopId = busRoute.DestinationBusStopId,
                    DestinationName = busRoute.DestinationName,
                    Direction = busRoute.Direction
                });
            }
            return new BusRoutesResponse() { BusRoutes = items };
        }


        public static BusJourneyResponse ToBusJourneyResponse(this BusJourney busJourney, Func<string, Stop?> getStopById)
        {
            List<BusCallingPointItemResponse> items = new List<BusCallingPointItemResponse>();
            foreach (BusCallingPoint busCallingPoint in busJourney.BusCallingPoints)
            {
                Stop? busStop = getStopById(busCallingPoint.BusStopId);
                if (busStop is null)
                    continue;

                items.Add(new BusCallingPointItemResponse()
                {
                    Sequence = busCallingPoint.Sequence,
                    BusStopId = busCallingPoint.BusStopId,
                    Latitude = busStop.Latitude,
                    Longitude = busStop.Longitude,
                    ScheduledTime = busCallingPoint.ScheduledTime,
                    Name = busStop.Name
                });
            }
            return new BusJourneyResponse() { 
                JourneyKey = busJourney.JourneyKey,
                OperatorName = busJourney.OperatorName,
                LineName = busJourney.LineName,
                Direction = busJourney.Direction,
                OriginName = busJourney.OriginName,
                OriginBusStopId = busJourney.OriginBusStopId,
                OriginDepartureTime = busJourney.OriginDepartureTime,
                DestinationName = busJourney.DestinationName,
                DestinationBusStopId = busJourney.DestinationBusStopId,
                DestinationArrivalTime = busJourney.DestinationArrivalTime,
                Latitude = busJourney.Latitude,
                Longitude = busJourney.Longitude,
                Bearing = busJourney.Bearing,
                ScheduleOffsetMinutes = busJourney.ScheduleOffsetMinutes,
                RecordedAtTime = busJourney.RecordedAtTime,
                BusCallingPoints = items 
            };
        }
    }
}