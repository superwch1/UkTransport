using Backend.Extensions;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;



// The xml sits next to the exe because Test.csproj copies *.xml to the output directory.
string path = Path.Combine(AppContext.BaseDirectory, "HIPK_199_HIPKPC000108653199_20260105_-_2204722.xml");

if (!File.Exists(path))
{
    Console.WriteLine($"File not found: {path}");
    return;
}

await using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
XElement root = document.Root ?? throw new InvalidDataException("Empty TransXChange document.");

XNamespace txc = "http://www.transxchange.org.uk/";

// Operators section
var operatordById = new Dictionary<string, (string NationalOperatorCode, string ShortName)>();
XElement operators = root.Element(txc + "Operators") ?? throw new InvalidDataException("<Operators> element not found.");

foreach (XElement busOperator in operators.Elements())
{
    string operatorId = busOperator.Attribute("id")?.Value ?? throw new InvalidDataException("<id> element not found.");
    string nationalOperatorCode = busOperator.Value(txc, "NationalOperatorCode") ?? throw new InvalidDataException("<NationalOperatorCode> element not found.");
    string operatorShortName = busOperator.Value(txc, "OperatorShortName") ?? throw new InvalidDataException("<OperatorShortName> element not found.");

    operatordById[operatorId] = (nationalOperatorCode,  operatorShortName);
}


// Journey Section
var journeyById = new Dictionary<string, (string SectionId, string direction, string origin, string destination)>();
IEnumerable<XElement> services = root.Element(txc + "Services")?.Elements(txc + "Service") ?? throw new InvalidDataException("<Services> element not found.");

foreach (XElement service in services)
{
    XElement standardService = service.Element(txc + "StandardService") ?? throw new InvalidDataException("<StandardService> element not found.");

    string origin = standardService.Value(txc, "Origin") ?? throw new InvalidDataException("<Origin> element not found.");
    string destination = standardService.Value(txc, "Destination") ?? throw new InvalidDataException("<Destination> element not found.");

    foreach (XElement journeyPattern in standardService.Elements(txc + "JourneyPattern"))
    {
        string journeyId = journeyPattern.Attribute("id")?.Value ?? throw new InvalidDataException("JourneyPattern 'id' attribute not found.");
        // string operatorId = journeyPattern.Value(txc, "OperatorRef") ?? throw new InvalidDataException("<OperatorRef> element not found.");
        string direction = journeyPattern.Value(txc, "Direction") ?? throw new InvalidDataException("<Direction> element not found.");

        string sectionId = journeyPattern
            .Elements(txc + "JourneyPatternSectionRefs")
            .Select(r => r.Value.Trim())
            .FirstOrDefault() ?? throw new InvalidDataException("<JourneyPatternSectionRefs> element not found.");

        journeyById[journeyId] = (sectionId, direction, origin, destination);
    }
}



// Line Section
var lineById = new Dictionary<string, string>();

foreach (XElement service in services)
{
    XElement lines = service.Element(txc + "Lines") ?? throw new InvalidDataException("<Lines> element not found.");

    foreach (XElement line in lines.Elements(txc + "Line"))
    {
        string lineId = line.Attribute("id")?.Value ?? throw new InvalidDataException("Line 'id' attribute not found.");
        string lineName = line.Value(txc, "LineName") ?? throw new InvalidDataException("<LineName> element not found.");

        lineById[lineId] = lineName;
    }
}



// Journey Pattern Section
var stopsBySectionId = new Dictionary<string, List<(string StopId, int Sequence, double MinutesFromDeparture)>>();
XElement journeyPatternSections = root.Element(txc + "JourneyPatternSections") ?? throw new InvalidDataException("<JourneyPatternSections> element not found.");

foreach (XElement journeyPatternSection in journeyPatternSections.Elements(txc + "JourneyPatternSection"))
{
    string sectionId = journeyPatternSection.Attribute("id")?.Value ?? throw new InvalidDataException("JourneyPatternSection 'id' attribute not found.");

    var stops = new List<(string StopId, int Sequence, double MinutesFromDeparture)>();

    // Minutes are measured from the departure at the first stop of the section.
    TimeSpan offsetFromDeparture = TimeSpan.Zero;
    int sequence = 1;

    List<XElement> timingLinks = journeyPatternSection.Elements(txc + "JourneyPatternTimingLink").ToList();
    if (timingLinks.Count == 0)
        throw new InvalidDataException("<JourneyPatternTimingLink> element not found");

    // Every link's To is the next link's From, so take the From of each link...
    foreach (XElement timingLink in timingLinks)
    {
        XElement from = timingLink.Element(txc + "From") ?? throw new InvalidDataException("<From> element not found.");
        string fromStopId = from.Value(txc, "StopPointRef") ?? throw new InvalidDataException("<StopPointRef> element not found.");

        stops.Add((fromStopId, sequence++, offsetFromDeparture.TotalMinutes));

        // A wait at the current stop before moving to the next stop.
        offsetFromDeparture += ParseDuration(from.Value(txc, "WaitTime"));
        offsetFromDeparture += ParseDuration(timingLink.Value(txc, "RunTime"));
    }

    XElement lastTo = timingLinks[timingLinks.Count - 1].Element(txc + "To") ?? throw new InvalidDataException("<To> element not found.");
    string lastStopId = lastTo.Value(txc, "StopPointRef") ?? throw new InvalidDataException("<StopPointRef> element not found.");

    stops.Add((lastStopId, sequence++, offsetFromDeparture.TotalMinutes));

    stopsBySectionId[sectionId] = stops;
}



// Vehicle Journey Section
XElement vehicleJourneys = root.Element(txc + "VehicleJourneys") ?? throw new InvalidDataException("<VehicleJourneys> element not found.");

foreach (XElement vehicleJourney in vehicleJourneys.Elements(txc + "VehicleJourney"))
{
    string operatorId = vehicleJourney.Value(txc, "OperatorRef") ?? throw new InvalidDataException("<OperatorRef> element not found.");
    string journeyId = vehicleJourney.Value(txc, "JourneyPatternRef") ?? throw new InvalidDataException("<JourneyPatternRef> element not found.");
    string lineId = vehicleJourney.Value(txc, "LineRef") ?? throw new InvalidDataException("<LineRef> element not found.");

    string departure = vehicleJourney.Value(txc, "DepartureTime") ?? throw new InvalidDataException("<DepartureTime> element not found.");
    TimeOnly departureTime = TimeOnly.Parse(departure, CultureInfo.InvariantCulture);

    if (!operatordById.TryGetValue(operatorId, out (string NationalOperatorCode, string ShortName) busOperator))
        throw new InvalidDataException($"Operator Id not found {operatorId}");

    if (!journeyById.TryGetValue(journeyId, out (string SectionId, string direction, string origin, string destination) journey))
        throw new InvalidDataException($"Journey Id not found {journeyId}");

    if (!stopsBySectionId.TryGetValue(journey.SectionId, out List<(string StopId, int Sequence, double MinutesFromDeparture)>? stops))
        throw new InvalidDataException($"Section Id not found {journey.SectionId}");

    if (!lineById.TryGetValue(lineId, out string? lineName) || lineName == null)
        throw new InvalidDataException($"Line Id not found {lineId}");

    Console.WriteLine($"Operator: {busOperator.NationalOperatorCode} - {busOperator.ShortName}");
    Console.WriteLine($"Origin: {journey.origin}, Destination: {journey.destination}, ({journey.direction})");
    foreach(var stop in stops)
    {
        // A journey crossing midnight wraps the clock, so keep the day it lands on.
        TimeOnly arrivalTime = departureTime.AddMinutes(stop.MinutesFromDeparture, out int arrivalDayOffset);

        Console.WriteLine($"bus stop Id: {stop.StopId} ({stop.Sequence}) - {arrivalTime} (+{arrivalDayOffset} day)");
    }
}



// Operating Profile Section
var profileByServiceCode = new Dictionary<string, (HashSet<DayOfWeek> Days, HashSet<string> BankHolidaysOfOperation, HashSet<string> BankHolidaysOfNonOperation)>();

foreach (XElement service in services)
{
    string serviceCode = service.Value(txc, "ServiceCode") ?? throw new InvalidDataException("<ServiceCode> element not found.");

    // A VehicleJourney may carry its own OperatingProfile, which replaces this one wholesale.
    XElement? operatingProfile = service.Element(txc + "OperatingProfile");

    // Both sides can be present at once: runs on some holidays, not on others.
    profileByServiceCode[serviceCode] = (
        ParseDaysOfWeek(operatingProfile),
        ParseBankHolidays(operatingProfile, "DaysOfOperation"),
        ParseBankHolidays(operatingProfile, "DaysOfNonOperation"));

    (HashSet<DayOfWeek> days, HashSet<string> alsoRunsOn, HashSet<string> doesNotRunOn) = profileByServiceCode[serviceCode];
    Console.WriteLine($"Service {serviceCode} runs {string.Join(", ", days)}");
    Console.WriteLine($"  also runs on: {string.Join(", ", alsoRunsOn)}");
    Console.WriteLine($"  does not run on: {string.Join(", ", doesNotRunOn)}");
}



// <DaysOfWeek> holds one empty element per day, but grouped and negated forms are equally valid.
HashSet<DayOfWeek> ParseDaysOfWeek(XElement? operatingProfile)
{
    var days = new HashSet<DayOfWeek>();

    XElement? regularDayType = operatingProfile?.Element(txc + "RegularDayType");

    // <HolidaysOnly/> means the journey never runs on a regular weekday.
    if (regularDayType?.Element(txc + "HolidaysOnly") is not null)
        return days;

    XElement? daysOfWeek = regularDayType?.Element(txc + "DaysOfWeek");
    if (daysOfWeek is null)
        return days;

    foreach (string dayOfWeekName in daysOfWeek.Elements().Select(x => x.Name.LocalName))
    {
        switch (dayOfWeekName)
        {
            case "Monday": days.Add(DayOfWeek.Monday); break;
            case "Tuesday": days.Add(DayOfWeek.Tuesday); break;
            case "Wednesday": days.Add(DayOfWeek.Wednesday); break;
            case "Thursday": days.Add(DayOfWeek.Thursday); break;
            case "Friday": days.Add(DayOfWeek.Friday); break;
            case "Saturday": days.Add(DayOfWeek.Saturday); break;
            case "Sunday": days.Add(DayOfWeek.Sunday); break;

            case "MondayToFriday": days.UnionWith(AllDaysExcept(DayOfWeek.Saturday, DayOfWeek.Sunday)); break;
            case "MondayToSaturday": days.UnionWith(AllDaysExcept(DayOfWeek.Sunday)); break;
            case "MondayToSunday": days.UnionWith(AllDaysExcept()); break;
            case "Weekend": days.Add(DayOfWeek.Saturday); days.Add(DayOfWeek.Sunday); break;

            case "NotMonday": days.UnionWith(AllDaysExcept(DayOfWeek.Monday)); break;
            case "NotTuesday": days.UnionWith(AllDaysExcept(DayOfWeek.Tuesday)); break;
            case "NotWednesday": days.UnionWith(AllDaysExcept(DayOfWeek.Wednesday)); break;
            case "NotThursday": days.UnionWith(AllDaysExcept(DayOfWeek.Thursday)); break;
            case "NotFriday": days.UnionWith(AllDaysExcept(DayOfWeek.Friday)); break;
            case "NotSaturday": days.UnionWith(AllDaysExcept(DayOfWeek.Saturday)); break;
            case "NotSunday": days.UnionWith(AllDaysExcept(DayOfWeek.Sunday)); break;

            default: throw new InvalidDataException($"<{dayOfWeekName}> is not a known DaysOfWeek value.");
        }
    }

    return days;
}


HashSet<string> ParseBankHolidays(XElement? operatingProfile, string operation)
{
    string[] christmasDays = ["ChristmasDay", "BoxingDay"];
    string[] otherBankHolidayDays = ["GoodFriday", "NewYearsDay", "Jan2ndScotland", "StAndrewsDay"];
    string[] holidayMondays = ["LateSummerBankHolidayNotScotland", "MayDay", "EasterMonday", "SpringBank", "AugustBankHolidayScotland"];
    string[] displacementHolidays = ["ChristmasDayHoliday", "BoxingDayHoliday", "NewYearsDayHoliday", "Jan2ndScotlandHoliday", "StAndrewsDayHoliday"];
    string[] earlyRunOffDays = ["ChristmasEve", "NewYearsEve"];

    var bankHolidays = new HashSet<string>();

    XElement? days = operatingProfile?.Element(txc + "BankHolidayOperation")?.Element(txc + operation);
    if (days is null)
        return bankHolidays;

    foreach (XElement day in days.Elements())
    {
        switch (day.Name.LocalName)
        {
            // Umbrella tags standing in for a whole group of days.
            case "AllBankHolidays":
                bankHolidays.UnionWith(christmasDays);
                bankHolidays.UnionWith(otherBankHolidayDays);
                bankHolidays.UnionWith(holidayMondays);
                bankHolidays.UnionWith(displacementHolidays);
                break;
            case "Christmas": bankHolidays.UnionWith(christmasDays); break;
            case "AllHolidaysExceptChristmas":
                bankHolidays.UnionWith(otherBankHolidayDays);
                bankHolidays.UnionWith(holidayMondays);
                break;
            case "HolidayMondays": bankHolidays.UnionWith(holidayMondays); break;
            case "DisplacementHolidays": bankHolidays.UnionWith(displacementHolidays); break;
            case "EarlyRunOffDays": bankHolidays.UnionWith(earlyRunOffDays); break;

            // Not an empty element: it carries a Description and an optional Date.
            case "OtherPublicHoliday":
                string description = day.Value(txc, "Description") ?? throw new InvalidDataException("<Description> element not found.");
                string? date = day.Value(txc, "Date");
                bankHolidays.Add(date is null ? description : $"{description} ({date})");
                break;

            default:
                if (!christmasDays.Contains(day.Name.LocalName)
                    && !otherBankHolidayDays.Contains(day.Name.LocalName)
                    && !holidayMondays.Contains(day.Name.LocalName)
                    && !displacementHolidays.Contains(day.Name.LocalName)
                    && !earlyRunOffDays.Contains(day.Name.LocalName))
                {
                    throw new InvalidDataException($"<{day.Name.LocalName}> is not a known bank holiday value.");
                }

                bankHolidays.Add(day.Name.LocalName);
                break;
        }
    }

    return bankHolidays;
}

static IEnumerable<DayOfWeek> AllDaysExcept(params DayOfWeek[] excluded)
{
    return Enum.GetValues<DayOfWeek>().Where(d => !excluded.Contains(d));
}

static TimeSpan ParseDuration(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return TimeSpan.Zero;

    return XmlConvert.ToTimeSpan(value);
}
