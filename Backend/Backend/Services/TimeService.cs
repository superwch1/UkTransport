namespace Backend.Services
{
    public class TimeService
    {
        private readonly TimeZoneInfo _ukTimeZone;

        private readonly TimeProvider _timeProvider;

        public TimeService(TimeProvider timeProvider)
        {
            _ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
            _timeProvider = timeProvider;
        }

        public DateTimeOffset UtcNowDateTimeOffset => _timeProvider.GetUtcNow(); // new DateTimeOffset(new DateTime(2026, 8, 2, 23, 10, 0) ) { };


        // UK wall-clock time, with the correct +00:00 or +01:00 offset baked in
        public DateTimeOffset UkNowDateTimeOffset => TimeZoneInfo.ConvertTime(UtcNowDateTimeOffset, _ukTimeZone);
        public DateTime UkNowDateTime => UkNowDateTimeOffset.DateTime;
        public DateOnly UkNowDateOnly => DateOnly.FromDateTime(UkNowDateTime);
        public TimeOnly UkNowTimeOnly => TimeOnly.FromDateTime(UkNowDateTime);


        public TimeZoneInfo UkTimeZone => _ukTimeZone;
    }
}
