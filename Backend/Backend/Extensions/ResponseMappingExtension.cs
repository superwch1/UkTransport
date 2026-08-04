using Backend.Models;

namespace Backend.Extensions
{
    public static class ResponseMappingExtension
    {
        //public static BusStopsResponse ToBusStopsResponse(this IReadOnlyList<Stop> busStops)
        //{
        //    List<BusStopItemResponse> items = new List<BusStopItemResponse>();
        //    foreach (Stop busStop in busStops)
        //    {
        //        items.Add(new BusStopItemResponse()
        //        {
        //            Id = busStop.Id,
        //            CommonName = busStop.Name,
        //            Bearing = busStop.Bearing,
        //            Latitude = busStop.Latitude,
        //            Longitude = busStop.Longitude,
        //        });
        //    }
        //    return new BusStopsResponse() { BusStops = items };
        //}


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


        public static BusTimetablesResponse ToBusTimetablesResponse(this Dictionary<string, List<BusTimetableItemResponse>> busTimetablesByStopPatternKey, DateTime now)
        {
            IEnumerable<List<BusTimetableItemResponse>> orderedStopPatterns = busTimetablesByStopPatternKey
                .Values
                .OrderByDescending(x => x.Count);

            int upcomingDepartureCount = 4;
            List<IReadOnlyList<BusTimetableItemResponse>> items = [];

            foreach (List<BusTimetableItemResponse> busTimetables in orderedStopPatterns)
            {
                int firstAfterNow = busTimetables.FindIndex(x => x.ScheduledDepartureTime > now);
                if (firstAfterNow < 0)
                    firstAfterNow = busTimetables.Count;

                items.Add(busTimetables.GetRange(0, Math.Min(firstAfterNow + upcomingDepartureCount, busTimetables.Count)));
            }

            return new BusTimetablesResponse() { BusTimetables = items };
        }
    }
}