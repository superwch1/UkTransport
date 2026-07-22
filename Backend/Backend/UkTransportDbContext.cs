using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend
{
    public class UkTransportDbContext(DbContextOptions<UkTransportDbContext> options) : DbContext(options)
    {
        public DbSet<BusTimetable> BusTimetables { get; set; }

        public DbSet<BusCallingPoint> BusCallingPoints { get; set; }
    }
}
