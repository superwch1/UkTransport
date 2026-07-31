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


        // UK wall-clock time, with the correct +00:00 or +01:00 offset baked in
        public DateTimeOffset UkNowDateTimeOffset => TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _ukTimeZone);

        // The same instant with no offset applied, for columns stored as timestamp with time zone, which Npgsql only
        // accepts in UTC.
        public DateTimeOffset UtcNowDateTimeOffset => _timeProvider.GetUtcNow();
        public DateTime UkNowDateTime => UkNowDateTimeOffset.DateTime;
        public DateOnly UkNowDateOnly => DateOnly.FromDateTime(UkNowDateTime);
        public TimeOnly UkNowTimeOnly => TimeOnly.FromDateTime(UkNowDateTime);


        public TimeZoneInfo UkTimeZone => _ukTimeZone;
    }
}
