using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend
{
    public class UkTransportDbContext(DbContextOptions<UkTransportDbContext> options) : DbContext(options)
    {
        public DbSet<BusDataset> BusDatasets { get; set; }

        public DbSet<BusTimetable> BusTimetables { get; set; }

        public DbSet<BusCallingPoint> BusCallingPoints { get; set; }

        public DbSet<BusSpecialDay> BusSpecialDays { get; set; }

        public DbSet<BusHoliday> BusHolidays { get; set; }

        public DbSet<PublicHoliday> PublicHolidays { get; set; }
    }
}
