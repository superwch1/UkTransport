using Backend.Enumerations;
using Backend.Models;
using Backend.Services;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using System.Collections.Immutable;
using System.Drawing;

namespace Backend.Repositories
{
    public class BusRepository
    {
        private readonly UkTransportDbContext _context;
        private readonly TransportDataStore _transportDataStore;
        private readonly TimeService _timeService;

        public BusRepository(UkTransportDbContext context, TransportDataStore transportDataStore, TimeService timeService)
        {
            _context = context;
            _transportDataStore = transportDataStore;
            _timeService = timeService;
        }


        public IReadOnlyList<BusRoute> GetBusRoutesByLineName(string lineName)
        {
            return _transportDataStore.BusRoutes
                .Where(x => x.LineName == lineName)
                .OrderBy(x => x.OperatorName)
                .ToList();
        }


        public async Task<ImmutableArray<BusRoute>> GetBusRoutes()
        {
            DateOnly todayDate = _timeService.UkNowDateOnly;
            IQueryable<BusTimetable> busTimetables = _context.BusTimetables.AsNoTracking();

            var routes = await busTimetables
                .ApplyScheduledDateFilter(todayDate, null, null)
                .Union(busTimetables
                    .ApplyScheduledDateFilter(todayDate.AddDays(-1), null, null))
                .GroupBy(x => new { x.RouteKey, x.Direction })
                .Select(g => new
                {
                    g.Key.RouteKey,
                    g.Key.Direction,
                    Representative = g.OrderByDescending(x => x.StartDate)
                        .Select(x => new { x.OriginBusStopId, x.DestinationBusStopId, x.LineName, x.OperatorName, x.DepartureTime, x.ArrivalTime, x.OriginName, x.DestinationName })
                        .First()
                })
                .ToListAsync();

            List<BusRoute> busRoutes = new List<BusRoute>();
            foreach (var route in routes)
            {
                busRoutes.Add(new BusRoute
                {
                    RouteKey = route.RouteKey,
                    LineName = route.Representative.LineName,
                    OperatorName = route.Representative.OperatorName,
                    OriginBusStopId = route.Representative.OriginBusStopId,
                    OriginName = route.Representative.OriginName,
                    DestinationBusStopId = route.Representative.DestinationBusStopId,
                    DestinationName = route.Representative.DestinationName,
                    Direction = route.Direction,
                    Duration = route.Representative.ArrivalTime - route.Representative.DepartureTime
                });
            }

            return busRoutes.ToImmutableArray();
        }


        public async Task<IReadOnlyDictionary<string, BusTimetable>> GetBusTimetableByJourneyKey(IEnumerable<string> journeyKey)
        {
            DateOnly todayDate = _timeService.UkNowDateOnly;
            DateOnly yesterdayDate = todayDate.AddDays(-1);

            // A bus reporting its position now set off either today or yesterday, so both operating days are candidates.
            IQueryable<BusTimetable> candidates = _context.BusTimetables
                .AsNoTracking()
                .Where(x => journeyKey.Contains(x.JourneyKey));
        
            // seems splitting query and doing grouping after materialize is much faster (from 25s to 0.5s)
            List<BusTimetable> timetables = await candidates
                .ApplyScheduledDateFilter(todayDate, null, null)
                .Union(candidates.ApplyScheduledDateFilter(yesterdayDate, null, null))
                .ToListAsync();

            if (timetables.Count == 0)
                return new Dictionary<string, BusTimetable>();

            // If several journeys still match, prefer the most recently-started schedule so repeated taps deterministically resolve to the current timetable version.
            timetables = timetables
                .GroupBy(x => x.JourneyKey)
                .Select(g => g.OrderByDescending(x => x.StartDate).First())
                .ToList();

            List<string> groupTimetableIds = timetables.Select(x => x.Id).ToList();

            Dictionary<string, List<BusCallingPoint>> callingPointsByTimetableId = await _context.BusCallingPoints
                .Where(x => groupTimetableIds.Contains(x.BusTimetableId))
                .GroupBy(x => x.BusTimetableId)
                .ToDictionaryAsync(x => x.Key, x => x.Select(x => x).OrderBy(x => x.Sequence).ToList());

            Dictionary<string, BusTimetable> result = [];
            foreach (var trip in timetables)
            {
                if (callingPointsByTimetableId.TryGetValue(trip.Id, out List<BusCallingPoint>? busCallingPoints))
                    result[trip.JourneyKey] = trip with { BusCallingPoints = busCallingPoints };
            }

            return result;
        }


        public async Task<Dictionary<string, List<BusTimetableItemResponse>>> GetBusTimetablesByRouteKey(string routeKey)
        {
            BusRoute? busRoute = _transportDataStore.BusRoutes
                .Where(x => x.RouteKey == routeKey)
                .FirstOrDefault();

            if (busRoute is null)
                return [];

            DateTime ukNow = _timeService.UkNowDateTime;
            TimeOnly ukTimeNow = _timeService.UkNowTimeOnly;

            DateOnly todayDate = DateOnly.FromDateTime(ukNow);
            DateOnly yesterdayDate = todayDate.AddDays(-1);

            // backward edge: departs >= now - 0.5h - duration, so a bus still on the road stays on the board or DELAYYY
            // forward edge: departs <= now + 4h, assuming the next bus is not more than a couple of hours off 
            TimeSpan delayBuffer = new TimeSpan(0, 30, 0);
            TimeSpan maxWaitForNextDeparture = new TimeSpan(4, 0, 0);

            TimeSpan yesterdayEarliestDeparture = new TimeSpan(24, 0, 0) + ukTimeNow.ToTimeSpan() - delayBuffer - busRoute.Duration; // 24 hours (1 day offset) - 0.5 hour delay - duration
            TimeSpan yesterdayLatestDeparture = new TimeSpan(24, 0, 0) + ukTimeNow.ToTimeSpan() + maxWaitForNextDeparture; // 24 hours (1 day offset) + current time + 4 hour buffer
            List<BusTimetable> yesterdayTimetables = await _context.BusTimetables
                .AsNoTracking()
                .Where(x => x.RouteKey == routeKey)
                .ApplyScheduledDateFilter(yesterdayDate, yesterdayEarliestDeparture, yesterdayLatestDeparture)
                .ToListAsync();

            TimeSpan todayDepartureWindowStart = ukTimeNow.ToTimeSpan() - delayBuffer - busRoute.Duration;
            TimeSpan todayDepartureWindowEnd = ukTimeNow.ToTimeSpan() + maxWaitForNextDeparture;
            List<BusTimetable> todayTimetables = await _context.BusTimetables
                .AsNoTracking()
                .Where(x => x.RouteKey == routeKey)
                .ApplyScheduledDateFilter(todayDate, todayDepartureWindowStart, todayDepartureWindowEnd)
                .ToListAsync();

            List<string> busTimetableIds = yesterdayTimetables
                .Select(x => x.Id)
                .Concat(todayTimetables.Select(x => x.Id))
                .Distinct()
                .ToList();

            if (busTimetableIds.Count == 0)
                return [];


            // Get the bus calling points and attach it back to the timetable
            Dictionary<string, List<BusCallingPoint>> callingPointsByTimetableId = await _context.BusCallingPoints
                .AsNoTracking()
                .Where(x => busTimetableIds.Contains(x.BusTimetableId))
                .GroupBy(x => x.BusTimetableId)
                .ToDictionaryAsync(x => x.Key, x => x.OrderBy(callingPoint => callingPoint.Sequence).ToList());


            // reason of using dictionary is because same origin and destination can have different bus stop inside journey
            Dictionary<string, List<BusTimetableItemResponse>> busTimetablesByStopPatternKey = [];
            GroupByStopPattern(busTimetablesByStopPatternKey, yesterdayTimetables, yesterdayDate, callingPointsByTimetableId);
            GroupByStopPattern(busTimetablesByStopPatternKey, todayTimetables, todayDate, callingPointsByTimetableId);

            foreach (List<BusTimetableItemResponse> stopPatternTimetables in busTimetablesByStopPatternKey.Values)
            {
                stopPatternTimetables.Sort((first, second) => first.ScheduledDepartureTime.CompareTo(second.ScheduledDepartureTime));
            }

            return busTimetablesByStopPatternKey;
        }


        private static void GroupByStopPattern(Dictionary<string, List<BusTimetableItemResponse>> busTimetablesByStopPatternKey,
            IReadOnlyList<BusTimetable> busTimetables, DateOnly date, IReadOnlyDictionary<string, List<BusCallingPoint>> callingPointsByTimetableId)
        {
            foreach (BusTimetable busTimetable in busTimetables)
            {
                if (!callingPointsByTimetableId.TryGetValue(busTimetable.Id, out List<BusCallingPoint>? busCallingPoints))
                    continue;

                List<BusCallingPointItemResponse> busCallingPointItems = new(busCallingPoints.Count);
                foreach (BusCallingPoint busCallingPoint in busCallingPoints)
                {
                    busCallingPointItems.Add(new BusCallingPointItemResponse()
                    {
                        Latitude = busCallingPoint.Latitude,
                        Longitude = busCallingPoint.Longitude,
                        Sequence = busCallingPoint.Sequence,
                        Name = busCallingPoint.Name,
                        ScheduledTime = date.ToDateTime(TimeOnly.MinValue) + busCallingPoint.ScheduledTime,
                    });
                }

                BusTimetableItemResponse busTimetableItem = new BusTimetableItemResponse()
                {
                    JourneyKey = busTimetable.JourneyKey,
                    ScheduledDepartureTime = busCallingPointItems[0].ScheduledTime,
                    CallingPoints = busCallingPointItems
                };

                if (!busTimetablesByStopPatternKey.TryGetValue(busTimetable.StopPatternKey, out List<BusTimetableItemResponse>? stopPatternTimetables))
                {
                    stopPatternTimetables = [];
                    busTimetablesByStopPatternKey[busTimetable.StopPatternKey] = stopPatternTimetables;
                }

                stopPatternTimetables.Add(busTimetableItem);
            }
        }


        public IReadOnlyList<LiveBusJourney> GetLiveBusJourneysByRouteKey(string routeKey)
        {
            return _transportDataStore
                .LiveBusJourneyByKey
                .Values
                .Where(x => x.RouteKey == routeKey)
                .ToList();
        }


        public IReadOnlyList<Stop> GetBusStops(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore
                .StopById
                .Values
                .Where(busStop => busStop.Latitude <= north && busStop.Latitude >= south && busStop.Longitude <= east && busStop.Longitude >= west)
                .ToList();
        }


        public async Task BulkInsertBusTimetables(IReadOnlyList<BusTimetable> busTimetables)
        {
            List<BusCallingPoint> callingPoints = busTimetables.SelectMany(x => x.BusCallingPoints ?? []).ToList();
            List<BusSpecialDay> specialDays = busTimetables.SelectMany(x => x.BusSpecialDays ?? []).ToList();
            List<BusHoliday> holidays = busTimetables.SelectMany(x => x.BusHolidays ?? []).ToList();

            // The holiday rows are keyed by name, so any name this file is the first to mention is upserted before the
            // journeys that point at it. Names already in the table are left as they are.
            List<PublicHoliday> publicHolidays = holidays
                .Select(x => x.PublicHolidayName)
                .Distinct()
                .Select(x => new PublicHoliday { Name = x })
                .ToList();

            // since it use bulk insert, it does not also insert the records inside collection
            await _context.BulkInsertAsync(busTimetables.ToList());
            await _context.BulkInsertAsync(callingPoints);
            await _context.BulkInsertAsync(specialDays);
            await _context.BulkInsertOrUpdateAsync(publicHolidays);
            await _context.BulkInsertAsync(holidays);
        }

        public async Task<BusDataset?> GetBusDataset(string datasetId)
        {
            return await _context.BusDatasets
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == datasetId);
        }

        public async Task ResetBusDataset(BusDataset busDataset)
        {
            await _context.BusDatasets
                .Where(x => x.Id == busDataset.Id)
                .ExecuteDeleteAsync();

            _context.BusDatasets.Add(busDataset);
            await _context.SaveChangesAsync();
        }

        public async Task CompleteBusDataset(string datasetId)
        {
            await _context.BusDatasets
                .Where(x => x.Id == datasetId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsImportCompleted, true));
        }
    }

    public static class BusRepositoryExtension
    {
        // A date falls in the nth occurrence of its own weekday, so days 1-7 are the first week, 8-14 the second,
        // and so on. The last occurrence is flagged as well, since the last Friday of a month is also its fourth
        // or fifth Friday and a journey may be stated either way.
        private static WeekOfMonth GetWeekOfMonth(DateOnly date)
        {
            WeekOfMonth week = ((date.Day - 1) / 7) switch
            {
                0 => WeekOfMonth.First,
                1 => WeekOfMonth.Second,
                2 => WeekOfMonth.Third,
                3 => WeekOfMonth.Fourth,
                _ => WeekOfMonth.Fifth,
            };

            if (date.Day + 7 > DateTime.DaysInMonth(date.Year, date.Month))
                week |= WeekOfMonth.Last;

            return week;
        }


        public static IQueryable<BusTimetable> ApplyScheduledDateFilter(this IQueryable<BusTimetable> query, DateOnly date, TimeSpan? earliestDeparture, TimeSpan? latestDeparture)
        {
            DayOfWeek dayOfWeek = date.DayOfWeek;
            WeekOfMonth weekOfMonth = GetWeekOfMonth(date);

            return query
                .Where(t => t.StartDate <= date && t.EndDate >= date)
                .Where(t => !t.BusSpecialDays!.Any(s => !s.IsOperating && s.StartDate <= date && s.EndDate >= date))
                .Where(t => earliestDeparture == null || t.DepartureTime >= earliestDeparture)
                .Where(t => latestDeparture == null || t.DepartureTime <= latestDeparture)

                // it can run on 3rd Aug (Friday) but mention not running on Friday in schedule 
                .Where(t => t.BusSpecialDays!
                    .Any(s => s.IsOperating && s.StartDate <= date && s.EndDate >= date) || 
                            ((t.WeeksOfMonth == WeekOfMonth.None || (t.WeeksOfMonth & weekOfMonth) != 0) && 
                                ((dayOfWeek == DayOfWeek.Monday && t.Monday) || 
                                (dayOfWeek == DayOfWeek.Tuesday && t.Tuesday) || 
                                (dayOfWeek == DayOfWeek.Wednesday && t.Wednesday) ||
                                (dayOfWeek == DayOfWeek.Thursday && t.Thursday) || 
                                (dayOfWeek == DayOfWeek.Friday && t.Friday) || 
                                (dayOfWeek == DayOfWeek.Saturday && t.Saturday) ||
                                (dayOfWeek == DayOfWeek.Sunday && t.Sunday))
                            )
                );
        }

    }
}
