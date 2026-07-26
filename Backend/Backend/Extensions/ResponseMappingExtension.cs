using Backend.Models;
using System.Collections.Frozen;

namespace Backend.Extensions
{
    public static class ResponseMappingExtension
    {
        public static BusLocationItemResponse ToBusLocationItemResponse(this BusLocation busLocation, FrozenDictionary<string, BusScheduleEstimate> busScheduleEstimateByKey)
        {
            int scheduleOffsetMinutes = 0;
            if (busScheduleEstimateByKey.TryGetValue(busLocation.OriginDepartureKey, out BusScheduleEstimate? estimate) && estimate != null)
                scheduleOffsetMinutes = estimate.ScheduleOffsetMinutes;

            return new BusLocationItemResponse()
            {
                OriginDepartureKey = busLocation.OriginDepartureKey,
                RecordedAtTime = busLocation.RecordedAtTime,
                OperatorRef = busLocation.OperatorRef,
                PublishedLineName = busLocation.PublishedLineName,
                OriginName = busLocation.OriginName,
                OriginRef = busLocation.OriginRef,
                OriginAimedDepartureTime = busLocation.OriginAimedDepartureTime,
                DestinationName = busLocation.DestinationName,
                DestinationRef = busLocation.DestinationRef,
                DestinationAimedArrivalTime = busLocation.DestinationAimedArrivalTime,
                EstimatedScheduleOffset = scheduleOffsetMinutes,
                Latitude = busLocation.Latitude,
                Longitude = busLocation.Longitude,
                Bearing = busLocation.Bearing
            };
        }

        public static BusLocationsResponse ToBusLocationsResponse(this IReadOnlyList<BusLocation> busLocations, FrozenDictionary<string, BusScheduleEstimate> busScheduleEstimateByKey)
        {
            List<BusLocationItemResponse> items = new List<BusLocationItemResponse>();
            foreach (BusLocation busLocation in busLocations)
            {
                items.Add(busLocation.ToBusLocationItemResponse(busScheduleEstimateByKey));
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
                    CommonName = busStop.CommonName,
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

                TimeOnly? scheduledTime = busCallingPoint.ArrivalTime ?? busCallingPoint.DepartureTime;
                if (busStop is null || scheduledTime is null)
                    continue;

                items.Add(new BusRouteItemResponse()
                {
                    Sequence = busCallingPoint.Sequence,
                    BusStopId = busCallingPoint.BusStopId,
                    Latitude = busStop.Latitude,
                    Longitude = busStop.Longitude,
                    ScheduledTime = scheduledTime.Value
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