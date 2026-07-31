using Backend.Models;
using System.Collections.Frozen;

namespace Backend.Extensions
{
    public static class ResponseMappingExtension
    {
        public static BusLocationItemResponse ToBusLocationItemResponse(this BusJourney busJourney, FrozenDictionary<string, BusJourney> busJourneyByKey)
        {
            int scheduleOffsetMinutes = 0;
            if (busJourneyByKey.TryGetValue(busJourney.TripScheduleKey, out BusJourney? journey) && journey != null)
                scheduleOffsetMinutes = journey.ScheduleOffsetMinutes;

            return new BusLocationItemResponse()
            {
                TripScheduleKey = busJourney.TripScheduleKey,
                RecordedAtTime = busJourney.RecordedAtTime,
                OperatorRef = busJourney.OperatorId,
                PublishedLineName = busJourney.LineName,
                OriginName = busJourney.OriginName,
                OriginRef = busJourney.OriginBusStopId,
                OriginAimedDepartureTime = busJourney.OriginAimedDepartureTime,
                DestinationName = busJourney.DestinationName,
                DestinationRef = busJourney.DestinationBusStopId,
                DestinationAimedArrivalTime = busJourney.DestinationAimedArrivalTime,
                EstimatedScheduleOffset = scheduleOffsetMinutes,
                Latitude = busJourney.Latitude,
                Longitude = busJourney.Longitude,
                Bearing = busJourney.Bearing
            };
        }

        public static BusLocationsResponse ToBusLocationsResponse(this IReadOnlyList<BusJourney> busJourneys, FrozenDictionary<string, BusJourney> busJourneyByKey)
        {
            List<BusLocationItemResponse> items = new List<BusLocationItemResponse>();
            foreach (BusJourney busJourney in busJourneys)
            {
                items.Add(busJourney.ToBusLocationItemResponse(busJourneyByKey));
            }
            return new BusLocationsResponse() { BusLocations = items };
        }

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


        public static BusRoutesResponse ToBusRoutesResponse(this IReadOnlyList<BusCallingPoint> busCallingPoints, FrozenDictionary<string, Stop> busStopById)
        {
            List<BusRouteItemResponse> items = new List<BusRouteItemResponse>();
            foreach (BusCallingPoint busCallingPoint in busCallingPoints)
            {
                if (!busStopById.TryGetValue(busCallingPoint.BusStopId, out Stop? busStop) || busStop is null)
                    continue;

                if (busStop is null)
                    continue;

                items.Add(new BusRouteItemResponse()
                {
                    Sequence = busCallingPoint.Sequence,
                    BusStopId = busCallingPoint.BusStopId,
                    Latitude = busStop.Latitude,
                    Longitude = busStop.Longitude,
                    ScheduledTime = busCallingPoint.ScheduledTime
                });
            }
            return new BusRoutesResponse() { BusRoutes = items };
        }


        public static BusStopTimetablesResponse ToBusCallingPointsResponse(this IReadOnlyDictionary<string, TimeOnly> timeByLineName)
        {
            List<BusStopTimetableItemResponse> items = new List<BusStopTimetableItemResponse>();
            foreach ((string lineName, TimeOnly scheduledTime) in timeByLineName)
            {
                items.Add(new BusStopTimetableItemResponse()
                {
                    LineName = lineName,
                    ScheduledTime = scheduledTime
                });
            }
            return new BusStopTimetablesResponse() { BusStopTimetables = items };
        }
    }
}