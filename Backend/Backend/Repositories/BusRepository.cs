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


        public async Task<IReadOnlyList<BusCallingPoint>> GetBusRoute(string originDepartureKey)
        {
            // LineName is intentionally NOT filtered. The live feed's PublishedLineName can differ from the timetable's LineName for the same physical route
            // If several journeys still match, prefer the most recently-started schedule so repeated taps deterministically resolve to the current timetable version.
            BusTimetable? timetable = await _context.BusTimetables
                .Include(x => x.BusCallingPoints)
                .Where(x => originDepartureKey == x.OriginDepartureKey &&
                            x.ValidFrom <= _timeService.UkNowDateOnly && x.ValidTo >= _timeService.UkNowDateOnly)
                .ApplyDayFilter(_timeService.UkNowDateTime, false)
                .AsNoTracking()
                .OrderByDescending(x => x.ValidFrom)
                .FirstOrDefaultAsync();

            if (timetable is null || timetable.BusCallingPoints is null)
                return [];

            return timetable.BusCallingPoints;
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<BusCallingPoint>>> GetBusRoutes(IEnumerable<string> originDepartureKeys)
        {
            // LineName is intentionally NOT filtered. The live feed's PublishedLineName can differ from the timetable's LineName for the same physical route
            // If several journeys still match, prefer the most recently-started schedule so repeated taps deterministically resolve to the current timetable version.
            IReadOnlyList<BusTimetable> timetables = await _context.BusTimetables
                .Include(x => x.BusCallingPoints)
                .Where(x => originDepartureKeys.Contains(x.OriginDepartureKey) &&
                            x.ValidFrom <= _timeService.UkNowDateOnly && x.ValidTo >= _timeService.UkNowDateOnly)
                .ApplyDayFilter(_timeService.UkNowDateTime, false)
                .AsNoTracking()
                .GroupBy(x => x.OriginDepartureKey)
                .Select(x => x.OrderByDescending(x => x.ValidFrom).First())
                .ToListAsync();

            if (timetables.Count == 0)
                return new Dictionary<string, IReadOnlyList<BusCallingPoint>>();

            return timetables.ToDictionary(x => x.OriginDepartureKey, x => x.BusCallingPoints ?? []);
        }

        public async Task<IReadOnlyDictionary<string, TimeOnly>> GetBusStopTimetable(string busStopId, DateTime now, bool isHoliday)
        {
            var nowTime = TimeOnly.FromDateTime(now);

            IQueryable<BusCallingPoint> query = _context.BusCallingPoints
                .Include(cp => cp.BusTimetable)
                .Where(cp => cp.BusStopId == busStopId)
                .Where(cp => (cp.DepartureTime != null && cp.DepartureTime >= nowTime) ||
                             (cp.ArrivalTime != null && cp.ArrivalTime >= nowTime));

            // Timetables referenced by those calling points.
            IQueryable<BusTimetable> timetables = query
                .Select(cp => cp.BusTimetable!)
                .Distinct()
                .ApplyDayFilter(_timeService.UkNowDateTime, false);


            // Keep only calling points whose timetable survived the day filter.
            var callingPoints = await query
                .Where(cp => timetables.Select(t => t.Id).Contains(cp.BusTimetableId))
                .ToListAsync();

            return callingPoints
                .GroupBy(cp => cp.BusTimetable!.LineName)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(cp => cp.ArrivalTime ?? cp.DepartureTime)
                        .OrderBy(x => x)
                        .First()!
                        .Value
                );
        }

        public BusLocation? GetBusLocationById(string busLocationId)
        {
            return _transportDataStore
                .BusLocationByKey
                .Values
                .FirstOrDefault(busLocation => busLocation.OriginDepartureKey == busLocationId);
        }


        public IReadOnlyList<BusLocation> GetBusLocations(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore
                .BusLocationByKey
                .Values
                .Where(busLocation => busLocation.Latitude <= north && busLocation.Latitude >= south && busLocation.Longitude <= east && busLocation.Longitude >= west)
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


        public async Task CreateBusTimetables(IReadOnlyList<BusTimetable> busTimetables)
        {
            List<BusCallingPoint> callingPoints = busTimetables.SelectMany(x => x.BusCallingPoints ?? []).ToList();

            // since it use bulk insert, it does not also insert the records inside collection in bus timetable
            await _context.BulkInsertAsync(busTimetables.ToList());
            await _context.BulkInsertAsync(callingPoints);
        }
    }

    public static class BusRepositoryExtension
    {
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

            return query.Where(t =>
                (t.ArrivalDayOffset == 0 && (
                    (today == DayOfWeek.Monday && t.Monday) ||
                    (today == DayOfWeek.Tuesday && t.Tuesday) ||
                    (today == DayOfWeek.Wednesday && t.Wednesday) ||
                    (today == DayOfWeek.Thursday && t.Thursday) ||
                    (today == DayOfWeek.Friday && t.Friday) ||
                    (today == DayOfWeek.Saturday && t.Saturday) ||
                    (today == DayOfWeek.Sunday && t.Sunday)
                )) ||
                (t.ArrivalDayOffset == 1 && (
                    (yesterday == DayOfWeek.Monday && t.Monday) ||
                    (yesterday == DayOfWeek.Tuesday && t.Tuesday) ||
                    (yesterday == DayOfWeek.Wednesday && t.Wednesday) ||
                    (yesterday == DayOfWeek.Thursday && t.Thursday) ||
                    (yesterday == DayOfWeek.Friday && t.Friday) ||
                    (yesterday == DayOfWeek.Saturday && t.Saturday) ||
                    (yesterday == DayOfWeek.Sunday && t.Sunday)
                )) ||
                (t.ArrivalDayOffset == 2 && (
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
