using Backend.Enumerations;
using Backend.Models;
using Backend.Services;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using System.Collections.Immutable;

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
            var routes = await _context.BusTimetables
                .AsNoTracking()
                .Where(x => x.StartDate <= _timeService.UkNowDateOnly && x.EndDate >= _timeService.UkNowDateOnly)
                .ApplyDayFilter(_timeService.UkNowDateTime, false)
                .GroupBy(x => new { x.OriginBusStopId, x.DestinationBusStopId, x.LineName, x.Direction })
                .Select(x => new
                {
                    x.Key.OriginBusStopId,
                    x.Key.DestinationBusStopId,
                    x.Key.LineName,
                    x.First().OperatorName,
                    x.First().Direction,
                    Duration = x.First().ArrivalTime < x.First().DepartureTime
                        ? (x.First().ArrivalTime - x.First().DepartureTime) + TimeSpan.FromHours(24)
                        : x.First().ArrivalTime - x.First().DepartureTime
                })
                .ToListAsync();

            List<BusRoute> busRoutes = new List<BusRoute>();
            foreach (var route in routes)
            {
                string originBusStopName = _transportDataStore.StopById.TryGetValue(route.OriginBusStopId, out Stop? originBusStop) && originBusStop is not null
                    ? originBusStop.Name
                    : route.OriginBusStopId;

                string destinationBusStopName = _transportDataStore.StopById.TryGetValue(route.DestinationBusStopId, out Stop? destinationBusStop) && destinationBusStop is not null
                   ? destinationBusStop.Name
                   : route.DestinationBusStopId;

                busRoutes.Add(new BusRoute
                {
                    RouteKey = BusTimeTableExtension.BuildRouteKey(route.LineName, route.OriginBusStopId, route.DestinationBusStopId),
                    LineName = route.LineName,
                    OperatorName = route.OperatorName,
                    OriginBusStopId = route.OriginBusStopId,
                    OriginName = originBusStopName,
                    DestinationBusStopId = route.DestinationBusStopId,
                    DestinationName = destinationBusStopName,
                    Direction = route.Direction,
                    Duration = route.Duration
                });
            }

            return busRoutes.ToImmutableArray();
        }


        public async Task<IReadOnlyDictionary<string, BusTimetable>> GetBusTimetableByKey(IEnumerable<string> journeyKey)
        {
            // If several journeys still match, prefer the most recently-started schedule so repeated taps deterministically resolve to the current timetable version.
            // seems splitting query is much faster (from 25s to 0.5s)
            List<BusTimetable> timetables = await _context.BusTimetables
                .AsNoTracking()
                .Where(x => journeyKey.Contains(x.JourneyKey) &&
                            x.StartDate <= _timeService.UkNowDateOnly && x.EndDate >= _timeService.UkNowDateOnly)
                .ApplyDayFilter(_timeService.UkNowDateTime, false)
                .ToListAsync();

            if (timetables.Count == 0)
                return new Dictionary<string, BusTimetable>();

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


        public async Task<IReadOnlyList<(DateOnly Date, IReadOnlyList<BusTimetable> BusTimetables)>> GetBusTimetablesByRouteKey(string routeKey)
        {
            BusRoute? busRoute = _transportDataStore.BusRoutes
                .Where(x => x.RouteKey == routeKey)
                .FirstOrDefault();

            if (busRoute is null)
                return [];

            TimeOnly nowTime = _timeService.UkNowTimeOnly;
            DateOnly todayDate = _timeService.UkNowDateOnly;
            DateOnly yesterdayDate = todayDate.AddDays(-1);
            TimeOnly noon = new TimeOnly(12, 0);

            // backward time: departs >= now - 1h - duration (a bus travel duration time to destination include 1 hour max delay)
            // forward time: departs <= now + 3h (next bus is with next 3 hours, assuming next bus need to wait for 2 hours)
            double backHours = 1.0 + busRoute.Duration.TotalHours; 
            TimeOnly backwardTime = nowTime.AddHours(-backHours, out int backwardWrappedDays);
            TimeOnly forwardTime = nowTime.AddHours(3, out int forwardEndWrappedDays);

            bool journeyStartsYesterday = backwardWrappedDays != 0;   // backward time fell into yesterday
            bool journeyEndsTomorrow = forwardEndWrappedDays != 0;    // now is 20:00 or later

            // no need to check for yesterday timetable after passing noon
            List<BusTimetable> yesterdayTimetables = nowTime > noon
                ? []
                : await _context.BusTimetables
                    .AsNoTracking()
                    .Where(x => x.RouteKey == routeKey)
                    .ApplyDepartureDateFilter(yesterdayDate)
                    .Where(x =>
                        // Departs yesterday, so only in range while the (Duration-widened) back edge reaches into it.
                        (x.ScheduledDayOffset == 0 && journeyStartsYesterday && x.DepartureTime > backwardTime) ||
                        // Departs today, 24:00+ form, bounded by whichever end of the window falls today.
                        (x.ScheduledDayOffset == 1 && (journeyStartsYesterday || x.DepartureTime > backwardTime) && (journeyEndsTomorrow || x.DepartureTime < forwardTime)))
                    .ToListAsync();

            List<BusTimetable> todayTimetables = await _context.BusTimetables
                .AsNoTracking()
                .Where(x => x.RouteKey == routeKey)
                .ApplyDepartureDateFilter(todayDate)
                .Where(x =>
                    // Departs today.
                    (x.ScheduledDayOffset == 0 && (journeyStartsYesterday || x.DepartureTime > backwardTime) && (journeyEndsTomorrow || x.DepartureTime < forwardTime)) ||
                    // Departs tomorrow, so only reachable once the front edge runs past midnight.
                    (x.ScheduledDayOffset == 1 && journeyEndsTomorrow && x.DepartureTime < forwardTime))
                .ToListAsync();

            yesterdayTimetables = DeduplicateByJourneyKey(yesterdayTimetables).ToList();
            todayTimetables = DeduplicateByJourneyKey(todayTimetables).ToList();


            List<string> busTimetableIds = yesterdayTimetables
                .Select(x => x.Id)
                .Concat(todayTimetables.Select(x => x.Id))
                .Distinct()
                .ToList();

            if (busTimetableIds.Count == 0)
                return [];

            Dictionary<string, List<BusCallingPoint>> callingPointsByTimetableId = await _context.BusCallingPoints
                .AsNoTracking()
                .Where(x => busTimetableIds.Contains(x.BusTimetableId))
                .GroupBy(x => x.BusTimetableId)
                .ToDictionaryAsync(x => x.Key, x => x.OrderBy(callingPoint => callingPoint.Sequence).ToList());

            return
            [
                (yesterdayDate, yesterdayTimetables.Select(x => AttachCallingPoints(x, callingPointsByTimetableId)).ToList()),
                (todayDate, todayTimetables.Select(x => AttachCallingPoints(x, callingPointsByTimetableId)).ToList())
            ];
        }


        private static BusTimetable AttachCallingPoints(BusTimetable busTimetable,
            IReadOnlyDictionary<string, List<BusCallingPoint>> callingPointsByTimetableId)
        {
            return callingPointsByTimetableId.TryGetValue(busTimetable.Id, out List<BusCallingPoint>? busCallingPoints)
                ? busTimetable with { BusCallingPoints = busCallingPoints }
                : busTimetable;
        }


        private static IEnumerable<BusTimetable> DeduplicateByJourneyKey(IEnumerable<BusTimetable> busTimetables)
        {
            return busTimetables
                .GroupBy(x => x.JourneyKey)
                .Select(x => x.OrderByDescending(busTimetable => busTimetable.StartDate).First())
                .OrderBy(x => x.DepartureTime);
        }


        public IReadOnlyList<LiveBusJourney> GetLiveBusJourneysByRouteKey(string routeKey)
        {
            return _transportDataStore
                .LiveBusJourneyByKey
                .Values
                .Where(x => x.RouteKey == routeKey)
                .ToList();
        }


        public LiveBusJourney? GetBusJourneyByKey(string journeyKey)
        {
            return _transportDataStore
                .LiveBusJourneyByKey
                .Values
                .FirstOrDefault(x => x.JourneyKey == journeyKey);
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

            // since it use bulk insert, it does not also insert the records inside collection in bus timetable
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


        /// <summary>
        /// Journeys scheduled to depart on <paramref name="date"/>, judged against that one date alone:
        /// its weekday, its week of the month, and any special days covering it. Each day a timetable is
        /// wanted for is queried separately, so a journey that runs every day comes back once per date
        /// rather than once in total, which is what keeps last night's 23:50 apart from tonight's.
        /// </summary>
        public static IQueryable<BusTimetable> ApplyDepartureDateFilter(this IQueryable<BusTimetable> query, DateOnly date)
        {
            DayOfWeek dayOfWeek = date.DayOfWeek;
            WeekOfMonth weekOfMonth = GetWeekOfMonth(date);

            // Dates of non-operation outrank everything else: where they conflict with any other rule,
            // including a date of operation, the journey is taken as not running.
            query = query.Where(t => !t.BusSpecialDays!.Any(s => !s.IsOperating &&
                s.StartDate <= date && s.EndDate >= date));

            // Dates of operation are additive and hold whatever weekday they land on, so they are ORed
            // with the regular days rather than narrowing them.
            return query.Where(t =>
                t.StartDate <= date && t.EndDate >= date &&
                (t.BusSpecialDays!.Any(s => s.IsOperating && s.StartDate <= date && s.EndDate >= date) ||
                 ((t.WeeksOfMonth == WeekOfMonth.None || (t.WeeksOfMonth & weekOfMonth) != 0) && (
                    (dayOfWeek == DayOfWeek.Monday && t.Monday) ||
                    (dayOfWeek == DayOfWeek.Tuesday && t.Tuesday) ||
                    (dayOfWeek == DayOfWeek.Wednesday && t.Wednesday) ||
                    (dayOfWeek == DayOfWeek.Thursday && t.Thursday) ||
                    (dayOfWeek == DayOfWeek.Friday && t.Friday) ||
                    (dayOfWeek == DayOfWeek.Saturday && t.Saturday) ||
                    (dayOfWeek == DayOfWeek.Sunday && t.Sunday)
                 ))));
        }


        public static IQueryable<BusTimetable> ApplyDayFilter(this IQueryable<BusTimetable> query, DateTime now, bool isHoliday)
        {
            // Build one candidate query per scenario, each pairing a specific
            // ArrivalDayOffset with the weekday it must have run on:
            //   offset 0 = journey running today
            //   offset 1 = overnight journey that departed yesterday, arriving now
            //   offset 2 = journey that departed two days ago
            DayOfWeek today = now.AddDays(0).DayOfWeek;
            DayOfWeek yesterday = now.AddDays(-1).DayOfWeek;
            DayOfWeek dayBeforeYesterday = now.AddDays(-2).DayOfWeek;

            // Special days are stated as the date the journey departs, so each offset is checked against the date
            // that offset started on, exactly as the weekdays below are.
            DateOnly todayDate = DateOnly.FromDateTime(now);
            DateOnly yesterdayDate = todayDate.AddDays(-1);
            DateOnly dayBeforeYesterdayDate = todayDate.AddDays(-2);

            // Which weeks of the month those dates fall in, worked out here so the database only has to compare
            // the stored flags against a constant.
            WeekOfMonth todayWeek = GetWeekOfMonth(todayDate);
            WeekOfMonth yesterdayWeek = GetWeekOfMonth(yesterdayDate);
            WeekOfMonth dayBeforeYesterdayWeek = GetWeekOfMonth(dayBeforeYesterdayDate);

            // Dates of non-operation outrank everything else: where they conflict with any other rule, including a
            // date of operation, the journey is taken as not running.
            query = query.Where(t => !t.BusSpecialDays!.Any(s => !s.IsOperating &&
                ((t.ScheduledDayOffset == 0 && s.StartDate <= todayDate && s.EndDate >= todayDate) ||
                 (t.ScheduledDayOffset == 1 && s.StartDate <= yesterdayDate && s.EndDate >= yesterdayDate) ||
                 (t.ScheduledDayOffset == 2 && s.StartDate <= dayBeforeYesterdayDate && s.EndDate >= dayBeforeYesterdayDate))));

            // Dates of operation are additive rather than a filter, and hold whatever weekday they land on, so they
            // are ORed with the regular days instead of narrowing them.
            return query.Where(t =>
                t.BusSpecialDays!.Any(s => s.IsOperating &&
                    ((t.ScheduledDayOffset == 0 && s.StartDate <= todayDate && s.EndDate >= todayDate) ||
                     (t.ScheduledDayOffset == 1 && s.StartDate <= yesterdayDate && s.EndDate >= yesterdayDate) ||
                     (t.ScheduledDayOffset == 2 && s.StartDate <= dayBeforeYesterdayDate && s.EndDate >= dayBeforeYesterdayDate))) ||
                (t.ScheduledDayOffset == 0 && (t.WeeksOfMonth == WeekOfMonth.None || (t.WeeksOfMonth & todayWeek) != 0) && (
                    (today == DayOfWeek.Monday && t.Monday) ||
                    (today == DayOfWeek.Tuesday && t.Tuesday) ||
                    (today == DayOfWeek.Wednesday && t.Wednesday) ||
                    (today == DayOfWeek.Thursday && t.Thursday) ||
                    (today == DayOfWeek.Friday && t.Friday) ||
                    (today == DayOfWeek.Saturday && t.Saturday) ||
                    (today == DayOfWeek.Sunday && t.Sunday)
                )) ||
                (t.ScheduledDayOffset == 1 && (t.WeeksOfMonth == WeekOfMonth.None || (t.WeeksOfMonth & yesterdayWeek) != 0) && (
                    (yesterday == DayOfWeek.Monday && t.Monday) ||
                    (yesterday == DayOfWeek.Tuesday && t.Tuesday) ||
                    (yesterday == DayOfWeek.Wednesday && t.Wednesday) ||
                    (yesterday == DayOfWeek.Thursday && t.Thursday) ||
                    (yesterday == DayOfWeek.Friday && t.Friday) ||
                    (yesterday == DayOfWeek.Saturday && t.Saturday) ||
                    (yesterday == DayOfWeek.Sunday && t.Sunday)
                )) ||
                (t.ScheduledDayOffset == 2 && (t.WeeksOfMonth == WeekOfMonth.None || (t.WeeksOfMonth & dayBeforeYesterdayWeek) != 0) && (
                    (dayBeforeYesterday == DayOfWeek.Monday && t.Monday) ||
                    (dayBeforeYesterday == DayOfWeek.Tuesday && t.Tuesday) ||
                    (dayBeforeYesterday == DayOfWeek.Wednesday && t.Wednesday) ||
                    (dayBeforeYesterday == DayOfWeek.Thursday && t.Thursday) ||
                    (dayBeforeYesterday == DayOfWeek.Friday && t.Friday) ||
                    (dayBeforeYesterday == DayOfWeek.Saturday && t.Saturday) ||
                    (dayBeforeYesterday == DayOfWeek.Sunday && t.Sunday)
                ))
            );
        }
    }
}
