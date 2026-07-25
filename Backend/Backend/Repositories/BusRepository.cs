using Backend.Models;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories
{
    public class BusRepository
    {
        private readonly UkTransportDbContext _context;
        private readonly TransportDataStore _transportDataStore;

        public BusRepository(UkTransportDbContext context, TransportDataStore transportDataStore)
        {
            _context = context;
            _transportDataStore = transportDataStore;
        }


        public IReadOnlyList<BusCallingPoint> GetBusRoute(string originDepartureKey, DateTime now, bool isHoliday)
        {
            IQueryable<BusTimetable> query = _context.BusTimetables
                .Where(x => originDepartureKey == null || originDepartureKey == x.OriginDepartureKey);

            // NOTE: LineName is intentionally NOT filtered. The live feed's PublishedLineName
            // can differ from the timetable's LineName for the same physical route
            // (e.g. feed "3" vs timetable "2"), so matching on stop + time + day is more reliable.

            // Runs on the right day / holiday.
            query = ApplyDayFilter(query, now, isHoliday);

            // Only schedules valid today.
            var today = DateOnly.FromDateTime(now);
            query = query.Where(t => t.ValidFrom <= today && t.ValidTo >= today);

            // If several journeys still match, prefer the most recently-started schedule
            // so repeated taps deterministically resolve to the current timetable version.
            // Resolve the winning id first: ApplyDayFilter concatenates several candidate
            // queries together, and EF Core cannot translate an Include's correlated
            // subquery on top of that set operation, so Include must happen in a
            // separate, plain query keyed on the id.
            string? timetableId = query
                .OrderByDescending(t => t.ValidFrom)
                .Select(t => t.Id)
                .FirstOrDefault();

            if (timetableId is null)
                return [];

            BusTimetable? timetable = _context.BusTimetables
                .AsNoTracking()
                .Include(x => x.BusCallingPoints)
                .FirstOrDefault(t => t.Id == timetableId);

            if (timetable is null)
                return [];

            return (timetable.BusCallingPoints ?? [])
                .OrderBy(x => x.Sequence)
                .ToList();
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
                .Distinct();

            // Original function, unchanged.
            timetables = ApplyDayFilter(timetables, now, isHoliday);

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


        private static IQueryable<BusTimetable> ApplyDayFilter(IQueryable<BusTimetable> query, DateTime now, bool isHoliday)
        {
            // Build one candidate query per scenario, each pairing a specific
            // ArrivalDayOffset with the weekday it must have run on:
            //   offset 0 = journey running today
            //   offset 1 = overnight journey that departed yesterday, arriving now
            //   offset 2 = journey that departed two days ago
            // The offset is passed by value into BuildDayCandidate so each deferred
            // query captures its own value (0, 1, 2) rather than sharing one loop
            // variable that would read 3 at execution time and match nothing.
            return BuildDayCandidate(query, now, 0)
                .Concat(BuildDayCandidate(query, now, 1))
                .Concat(BuildDayCandidate(query, now, 2));
        }


        private static IQueryable<BusTimetable> BuildDayCandidate(IQueryable<BusTimetable> query, DateTime now, int offset)
        {
            DayOfWeek operatingDay = now.AddDays(-offset).DayOfWeek;
            IQueryable<BusTimetable> candidate = query.Where(t => t.ArrivalDayOffset == offset);

            return operatingDay switch
            {
                DayOfWeek.Monday => candidate.Where(t => t.Monday),
                DayOfWeek.Tuesday => candidate.Where(t => t.Tuesday),
                DayOfWeek.Wednesday => candidate.Where(t => t.Wednesday),
                DayOfWeek.Thursday => candidate.Where(t => t.Thursday),
                DayOfWeek.Friday => candidate.Where(t => t.Friday),
                DayOfWeek.Saturday => candidate.Where(t => t.Saturday),
                DayOfWeek.Sunday => candidate.Where(t => t.Sunday),
                _ => candidate,
            };
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


        public IReadOnlyList<BusStop> GetBusStops(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore
                .BusStopById
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
}
