using Backend.Models;

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
                    RouteKey = busRoute.RouteKey,
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


        public static LiveBusJourneysResponse ToLiveBusJourneysResponse(this IReadOnlyList<LiveBusJourney> busJourneys)
        {
            List<LiveBusJourneyItemResponse> items = new List<LiveBusJourneyItemResponse>();
            foreach (LiveBusJourney busJourney in busJourneys)
            {
                items.Add(new LiveBusJourneyItemResponse()
                {
                    JourneyKey = busJourney.JourneyKey,
                    Latitude = busJourney.Latitude,
                    Longitude = busJourney.Longitude,
                    Bearing = busJourney.Bearing,
                    ScheduleOffsetMinutes = busJourney.ScheduleOffsetMinutes,
                    RecordedAtTime = busJourney.RecordedAtTime
                });
            }
            return new LiveBusJourneysResponse() { LiveBusJourneys = items };
        }


        public static BusTimetablesResponse ToBusTimetablesResponse(this IReadOnlyList<(DateOnly Date, IReadOnlyList<BusTimetable> BusTimetables)> busTimetablesByDate, Func<string, Stop?> getStopById)
        {
            List<BusTimetableItemResponse> items = new List<BusTimetableItemResponse>();
            foreach (var (date, busTimetables) in busTimetablesByDate)
            {
                foreach (BusTimetable busTimetable in busTimetables)
                {
                    if (busTimetable.BusCallingPoints is null)
                        continue;

                    List<BusCallingPointItemResponse> callingPoints = [];
                    foreach(var point in busTimetable.BusCallingPoints)
                    {
                        Stop? stop = getStopById(point.BusStopId);
                        if (stop == null)
                            continue;

                        callingPoints.Add(new BusCallingPointItemResponse()
                        {
                            Sequence = point.Sequence,
                            BusStopId = point.BusStopId,
                            ScheduledTime = date.ToDateTime(TimeOnly.MinValue) + point.ScheduledTime,
                            Latitude = stop.Latitude,
                            Longitude = stop.Longitude,
                            Name = stop.Name
                        });
                    }

                    items.Add(new BusTimetableItemResponse()
                    {
                        JourneyKey = busTimetable.JourneyKey,
                        RouteKey = busTimetable.RouteKey,
                        Direction = busTimetable.Direction,
                        CallingPoints = callingPoints
                    });
                }
            }
            return new BusTimetablesResponse() { BusTimetables = items };
        }
    }
}