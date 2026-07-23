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


        public IReadOnlyList<BusCallingPoint> GetBusRoute(string busLocationId, DateTime now, bool isHoliday)
        {
            BusLocation? busLocation = GetBusLocationById(busLocationId);
            if (busLocation is null || (busLocation.OriginAimedDepartureTime is null && busLocation.DestinationAimedArrivalTime is null))
                return [];

            var originTime = busLocation.OriginAimedDepartureTime;
            var destTime = busLocation.DestinationAimedArrivalTime;

            IQueryable<BusTimetable> query = _context.BusCallingPoints
                // origin calling point: matching stop, exact departure time
                .Where(origin => origin.BusStopId == busLocation.OriginRef &&
                    (originTime == null || origin.DepartureTime == originTime))

                .Join(_context.BusCallingPoints,
                    origin => origin.BusTimetableId,
                    destination => destination.BusTimetableId,
                    (origin, destination) => new { origin, destination })

                // destination calling point: matching stop, later in journey, exact arrival time
                .Where(x => x.destination.BusStopId == busLocation.DestinationRef &&
                            x.origin.Sequence < x.destination.Sequence &&
                    (destTime == null || x.destination.ArrivalTime == destTime))

                .Join(_context.BusTimetables,
                    x => x.origin.BusTimetableId,
                    timetable => timetable.Id,
                    (x, timetable) => timetable);

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
            string? timetableId = query
                .OrderByDescending(t => t.ValidFrom)
                .Select(t => t.Id)
                .FirstOrDefault();

            if (timetableId is null)
                return [];

            return _context.BusCallingPoints
                .AsNoTracking()
                .Where(x => x.BusTimetableId == timetableId)
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
            if (isHoliday)
                return query.Where(t => t.RunsOnBankHolidays);

            else
            {
                return now.DayOfWeek switch
                {
                    DayOfWeek.Monday => query.Where(t => t.Monday),
                    DayOfWeek.Tuesday => query.Where(t => t.Tuesday),
                    DayOfWeek.Wednesday => query.Where(t => t.Wednesday),
                    DayOfWeek.Thursday => query.Where(t => t.Thursday),
                    DayOfWeek.Friday => query.Where(t => t.Friday),
                    DayOfWeek.Saturday => query.Where(t => t.Saturday),
                    DayOfWeek.Sunday => query.Where(t => t.Sunday),
                    _ => query,
                };
            }
                
        }


        public BusLocation? GetBusLocationById(string busLocationId)
        {
            return _transportDataStore.GetBusLocations()
                .FirstOrDefault(busLocation => busLocation.Id == busLocationId);
        }


        public IReadOnlyList<BusLocation> GetBusLocations(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore.GetBusLocations()
                .Where(busLocation => busLocation.Latitude <= north && busLocation.Latitude >= south && busLocation.Longitude <= east && busLocation.Longitude >= west)
                .ToList();
        }


        public IReadOnlyList<BusStop> GetBusStops(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore.GetBusStops()
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
