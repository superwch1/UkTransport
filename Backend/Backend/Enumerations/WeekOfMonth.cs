namespace Backend.Enumerations
{
    // Weeks of the month a journey is confined to, ANDed with its regular days: "first and third Wednesdays".
    // None means no restriction, which is the case for almost every journey.
    [Flags]
    public enum WeekOfMonth
    {
        None = 0,

        First = 1 << 0,
        Second = 1 << 1,
        Third = 1 << 2,
        Fourth = 1 << 3,
        Fifth = 1 << 4,

        // The final occurrence of the weekday in the month, which is also its fourth or fifth occurrence.
        Last = 1 << 5,
    }
}
