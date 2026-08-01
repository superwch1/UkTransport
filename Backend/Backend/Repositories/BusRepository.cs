using Backend.Enumerations;
using Backend.Models;
using Backend.Services;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;

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


        public async Task<IReadOnlyList<BusCallingPoint>> GetBusRoute(string journeyKey)
        {
            // If several journeys still match, prefer the most recently-started schedule so repeated taps deterministically resolve to the current timetable version.
            BusTimetable? timetable = await _context.BusTimetables
                .Include(x => x.BusCallingPoints)
                .Where(x => journeyKey == x.JourneyKey &&
                            x.StartDate <= _timeService.UkNowDateOnly && x.EndDate >= _timeService.UkNowDateOnly)
                .ApplyDayFilter(_timeService.UkNowDateTime, false)
                .AsNoTracking()
                .OrderByDescending(x => x.StartDate)
                .FirstOrDefaultAsync();   

            if (timetable is null || timetable.BusCallingPoints is null)
                return [];

            return timetable.BusCallingPoints;
        }


        public async Task<IReadOnlyDictionary<string, IReadOnlyList<BusRoute>>> GetBusOriginDestinations()
        {
            var routes = await _context.BusTimetables
                .AsNoTracking()
                .Where(x => x.StartDate <= _timeService.UkNowDateOnly && x.EndDate >= _timeService.UkNowDateOnly)
                .ApplyDayFilter(_timeService.UkNowDateTime, false)
                .GroupBy(x => new { x.OriginBusStopId, x.DestinationBusStopId, x.LineName })
                .Select(x => new
                {
                    x.Key.OriginBusStopId,
                    x.Key.DestinationBusStopId,
                    x.Key.LineName,
                    x.First().OperatorName,
                    x.First().Direction
                })
                .OrderBy(x => x.OperatorName)
                .ToListAsync();

            Dictionary<string, List<BusRoute>> routeByLineName = new Dictionary<string, List<BusRoute>>(StringComparer.Ordinal);
            foreach (var route in routes)
            {
                string originBusStopName = _transportDataStore.StopById.TryGetValue(route.OriginBusStopId, out Stop? originBusStop) && originBusStop is not null
                    ? originBusStop.Name
                    : route.OriginBusStopId;

                string destinationBusStopName = _transportDataStore.StopById.TryGetValue(route.DestinationBusStopId, out Stop? destinationBusStop) && destinationBusStop is not null
                   ? destinationBusStop.Name
                   : route.DestinationBusStopId;

                if (!routeByLineName.TryGetValue(route.LineName, out List<BusRoute>? busRoutes))
                {
                    busRoutes = [];
                    routeByLineName[route.LineName] = busRoutes;
                }

                busRoutes.Add(new BusRoute
                {
                    LineName = route.LineName,
                    OperatorName = route.OperatorName,
                    OriginBusStopId = route.OriginBusStopId,
                    OriginName = originBusStopName,
                    DestinationBusStopId = route.DestinationBusStopId,
                    DestinationName = destinationBusStopName,
                    Direction = route.Direction
                });
            }

            return routeByLineName.ToDictionary(x => x.Key, x => (IReadOnlyList<BusRoute>)x.Value);
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
                if (callingPointsByTimetableId.TryGetValue(trip.Id, out List<BusCallingPoint>? journeyCallingPoints))
                    result[trip.JourneyKey] = trip with { BusCallingPoints = journeyCallingPoints };
            }

            return result;
        }


        public BusJourney? GetBusJourneyByKey(string journeyKey)
        {
            return _transportDataStore
                .BusJourneyByKey
                .Values
                .FirstOrDefault(x => x.JourneyKey == journeyKey);
        }


        public IReadOnlyList<BusJourney> GetBusJourneys(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore
                .BusJourneyByKey
                .Values
                .Where(x => x.Latitude <= north && x.Latitude >= south && x.Longitude <= east && x.Longitude >= west)
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
