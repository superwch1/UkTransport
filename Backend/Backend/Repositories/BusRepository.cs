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
            // The catalogue is every route operating today, so it is judged on the operating date alone. Filtering on
            // arrival instead would both keep yesterday-only routes alive on the strength of an 00:50 arrival and lose
            // routes whose only journeys today leave late and arrive tomorrow.
            var routes = await _context.BusTimetables
                .AsNoTracking()
                .ApplyDepartureDateFilter(_timeService.UkNowDateOnly)
                .GroupBy(x => new { x.OriginBusStopId, x.DestinationBusStopId, x.LineName, x.Direction })
                .Select(x => new
                {
                    x.Key.OriginBusStopId,
                    x.Key.DestinationBusStopId,
                    x.Key.LineName,
                    x.First().OperatorName,
                    x.First().Direction,
                    x.First().DepartureTime,
                    x.First().ArrivalTime,
                    x.First().DepartureDayOffset,
                    x.First().ArrivalDayOffset
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

                // Both ends carry their own day offset, so the span is read off them rather than guessed from an
                // arrival clock that reads earlier than the departure. Worked in TimeSpan so the subtraction stays
                // signed instead of wrapping the way TimeOnly subtraction does.
                TimeSpan duration = (route.ArrivalTime.ToTimeSpan() - route.DepartureTime.ToTimeSpan())
                    + TimeSpan.FromDays(route.ArrivalDayOffset - route.DepartureDayOffset);

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
                    Duration = duration
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
                .Where(x => journeyKey.Contains(x.JourneyKey))
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

            // One read of the clock, so a request either side of midnight cannot pair one day's date with another day's time.
            DateTime ukNow = _timeService.UkNowDateTime;
            TimeOnly nowTime = TimeOnly.FromDateTime(ukNow);
            DateOnly todayDate = DateOnly.FromDateTime(ukNow);
            DateOnly yesterdayDate = todayDate.AddDays(-1);
            DateOnly tomorrowDate = todayDate.AddDays(1);

            // backward time: departs >= now - 1h - duration (a bus travel duration time to destination include 1 hour max delay)
            // forward time: departs <= now + 3h (next bus is with next 3 hours, assuming next bus need to wait for 2 hours)
            double backHours = 1.0 + busRoute.Duration.TotalHours;
            TimeOnly backwardTime = nowTime.AddHours(-backHours, out int backwardWrappedDays);
            TimeOnly forwardTime = nowTime.AddHours(3, out int forwardWrappedDays);

            bool windowStartsYesterday = backwardWrappedDays != 0;   // back edge ran past midnight into yesterday
            bool windowEndsTomorrow = forwardWrappedDays != 0;       // now is 21:00 or later

            // A row's operating date is what ApplyDepartureDateFilter matches on, and the bus leaves DepartureDayOffset
            // days after it, so one operating date can hold departures landing on two calendar days:
            //   yesterday + offset 0 -> leaves yesterday    today + offset 0 -> leaves today
            //   yesterday + offset 1 -> leaves today        today + offset 1 -> leaves tomorrow
            //   tomorrow  + offset 0 -> leaves tomorrow
            // Departures landing yesterday need the back edge to reach them, ones landing tomorrow need the front edge,
            // and ones landing today are held between whichever edges fall today.
            List<BusTimetable> yesterdayTimetables = await _context.BusTimetables
                .AsNoTracking()
                .Where(x => x.RouteKey == routeKey)
                .ApplyDepartureDateFilter(yesterdayDate)
                .Where(x =>
                    (x.DepartureDayOffset == 0 && windowStartsYesterday && x.DepartureTime >= backwardTime) ||
                    (x.DepartureDayOffset == 1 && (windowStartsYesterday || x.DepartureTime >= backwardTime) && (windowEndsTomorrow || x.DepartureTime <= forwardTime)))
                .ToListAsync();

            List<BusTimetable> todayTimetables = await _context.BusTimetables
                .AsNoTracking()
                .Where(x => x.RouteKey == routeKey)
                .ApplyDepartureDateFilter(todayDate)
                .Where(x =>
                    (x.DepartureDayOffset == 0 && (windowStartsYesterday || x.DepartureTime >= backwardTime) && (windowEndsTomorrow || x.DepartureTime <= forwardTime)) ||
                    (x.DepartureDayOffset == 1 && windowEndsTomorrow && x.DepartureTime <= forwardTime))
                .ToListAsync();

            // A night bus stated as 00:30 belongs to the day it departs, so it sits on tomorrow's operating date and is
            // only reachable once the front edge runs past midnight.
            List<BusTimetable> tomorrowTimetables = !windowEndsTomorrow
                ? []
                : await _context.BusTimetables
                    .AsNoTracking()
                    .Where(x => x.RouteKey == routeKey)
                    .ApplyDepartureDateFilter(tomorrowDate)
                    .Where(x => x.DepartureDayOffset == 0 && x.DepartureTime <= forwardTime)
                    .ToListAsync();

            yesterdayTimetables = DeduplicateByJourneyKey(yesterdayTimetables).ToList();
            todayTimetables = DeduplicateByJourneyKey(todayTimetables).ToList();
            tomorrowTimetables = DeduplicateByJourneyKey(tomorrowTimetables).ToList();


            List<string> busTimetableIds = yesterdayTimetables
                .Select(x => x.Id)
                .Concat(todayTimetables.Select(x => x.Id))
                .Concat(tomorrowTimetables.Select(x => x.Id))
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
                (todayDate, todayTimetables.Select(x => AttachCallingPoints(x, callingPointsByTimetableId)).ToList()),
                (tomorrowDate, tomorrowTimetables.Select(x => AttachCallingPoints(x, callingPointsByTimetableId)).ToList())
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
                .OrderBy(x => x.DepartureDayOffset)
                .ThenBy(x => x.DepartureTime);
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
            DayOfWeek today = now.AddDays(0).DayOfWeek;
            DayOfWeek yesterday = now.AddDays(-1).DayOfWeek;

            DateOnly todayDate = DateOnly.FromDateTime(now);
            WeekOfMonth todayWeek = GetWeekOfMonth(todayDate);

            DateOnly yesterdayDate = todayDate.AddDays(-1);
            WeekOfMonth yesterdayWeek = GetWeekOfMonth(yesterdayDate);

            // Dates of non-operation outrank everything else: where they conflict with any other rule, including a
            // date of operation, the journey is taken as not running.
            query = query.Where(t => !t.BusSpecialDays!.Any(s => !s.IsOperating &&
                ((t.DepartureDayOffset == 0 && s.StartDate <= todayDate && s.EndDate >= todayDate) ||
                 (t.ArrivalDayOffset == 1 && s.StartDate <= yesterdayDate && s.EndDate >= yesterdayDate))));

            // Dates of operation are additive rather than a filter, and hold whatever weekday they land on, so they
            // are ORed with the regular days instead of narrowing them.
            return query.Where(t =>
                t.BusSpecialDays!.Any(s => s.IsOperating &&
                    ((t.DepartureDayOffset == 0 && s.StartDate <= todayDate && s.EndDate >= todayDate) ||
                     (t.ArrivalDayOffset == 1 && s.StartDate <= yesterdayDate && s.EndDate >= yesterdayDate))) ||
                (t.DepartureDayOffset == 0 && (t.WeeksOfMonth == WeekOfMonth.None || (t.WeeksOfMonth & todayWeek) != 0) && (
                    (today == DayOfWeek.Monday && t.Monday) ||
                    (today == DayOfWeek.Tuesday && t.Tuesday) ||
                    (today == DayOfWeek.Wednesday && t.Wednesday) ||
                    (today == DayOfWeek.Thursday && t.Thursday) ||
                    (today == DayOfWeek.Friday && t.Friday) ||
                    (today == DayOfWeek.Saturday && t.Saturday) ||
                    (today == DayOfWeek.Sunday && t.Sunday)
                )) ||
                (t.ArrivalDayOffset == 1 && (t.WeeksOfMonth == WeekOfMonth.None || (t.WeeksOfMonth & yesterdayWeek) != 0) && (
                    (yesterday == DayOfWeek.Monday && t.Monday) ||
                    (yesterday == DayOfWeek.Tuesday && t.Tuesday) ||
                    (yesterday == DayOfWeek.Wednesday && t.Wednesday) ||
                    (yesterday == DayOfWeek.Thursday && t.Thursday) ||
                    (yesterday == DayOfWeek.Friday && t.Friday) ||
                    (yesterday == DayOfWeek.Saturday && t.Saturday) ||
                    (yesterday == DayOfWeek.Sunday && t.Sunday)
                ))
            );
        }
    }
}
