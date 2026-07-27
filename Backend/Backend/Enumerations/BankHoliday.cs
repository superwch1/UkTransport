namespace Backend.Enumerations
{
    [Flags]
    public enum BankHoliday
    {
        None = 0,

        ChristmasDay = 1 << 0,
        BoxingDay = 1 << 1,

        GoodFriday = 1 << 2,
        NewYearsDay = 1 << 3,
        Jan2ndScotland = 1 << 4,
        StAndrewsDay = 1 << 5,

        LateSummerBankHolidayNotScotland = 1 << 6,
        MayDay = 1 << 7,
        EasterMonday = 1 << 8,
        SpringBank = 1 << 9,
        AugustBankHolidayScotland = 1 << 10,

        ChristmasDayHoliday = 1 << 11,
        BoxingDayHoliday = 1 << 12,
        NewYearsDayHoliday = 1 << 13,
        Jan2ndScotlandHoliday = 1 << 14,
        StAndrewsDayHoliday = 1 << 15,

        // Not bank holidays — services finish early.
        ChristmasEve = 1 << 16,
        NewYearsEve = 1 << 17,
    }
}
