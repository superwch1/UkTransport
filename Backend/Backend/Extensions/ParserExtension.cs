using Backend.Enumerations;
using Backend.Models;
using System.Globalization;
using System.Xml.Linq;

namespace Backend.Extensions
{
    public static class ParserExtension
    {
        public static async Task<Dictionary<string, BusLocation>> ParseBusLocation(this Stream stream, XNamespace xmlNamespace, string unknownPlaceholder, TimeZoneInfo timeZoneInfo, ILogger logger, CancellationToken cancellationToken)
        {
            Dictionary<string, BusLocation> busLocations = [];
            XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            IEnumerable<XElement> activities = document.Descendants(xmlNamespace + "VehicleActivity");
            foreach (XElement activity in activities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    DateTime? recordedAtTime = activity.Value(xmlNamespace, "RecordedAtTime").ParseUkDateTime(timeZoneInfo);
                    if (recordedAtTime is null || DateTime.Now - TimeSpan.FromMinutes(10) > recordedAtTime.Value)
                        continue;

                    XElement journey = activity.Element(xmlNamespace + "MonitoredVehicleJourney") ?? throw new InvalidDataException("<MonitoredVehicleJourney> element not found.");

                    string? operatorRef = journey.Value(xmlNamespace, "OperatorRef");
                    string? publishedLineName = journey.Value(xmlNamespace, "PublishedLineName");

                    string? originRef = journey.Value(xmlNamespace, "OriginRef");
                    string? destinationRef = journey.Value(xmlNamespace, "DestinationRef");

                    // possible value: inbound, outbound, clockwise, anticlockwise, 1, 2
                    string? directionRef = journey.Value(xmlNamespace, "DirectionRef");

                    if (operatorRef is null || publishedLineName is null || originRef is null || destinationRef is null || directionRef is null)
                        continue;

                    string originName = journey.Value(xmlNamespace, "OriginName") ?? unknownPlaceholder;
                    string destinationName = journey.Value(xmlNamespace, "DestinationName") ?? unknownPlaceholder;

                    TimeOnly? originAimedDepartureTime = journey.Value(xmlNamespace, "OriginAimedDepartureTime").ParseUkTimeOnly(timeZoneInfo);
                    TimeOnly? destinationAimedArrivalTime = journey.Value(xmlNamespace, "DestinationAimedArrivalTime").ParseUkTimeOnly(timeZoneInfo);

                    string? journeyCode = activity
                        .Element(xmlNamespace + "Extensions")?
                        .Element(xmlNamespace + "Operational")?
                        .Element(xmlNamespace + "TicketMachine")?
                        .Value(xmlNamespace, "JourneyCode");

                    TimeOnly? departureTimeFromJourneyCode = (journeyCode is null || journeyCode == "0000")
                        ? null
                        : journeyCode.ParseTimeOnly(format: ["HHmm", "HH:mm:ss"]); // "0000" is that machine's null sentinel

                    // seems the bus either do not have arrival time or both arrival and departure time
                    originAimedDepartureTime = originAimedDepartureTime ?? departureTimeFromJourneyCode;
                    if (!originAimedDepartureTime.HasValue)
                        continue;

                    XElement? location = journey?.Element(xmlNamespace + "VehicleLocation");
                    decimal? latitude = location.Value(xmlNamespace, "Latitude").ParseDecimal();
                    decimal? longitude = location.Value(xmlNamespace, "Longitude").ParseDecimal();
                    decimal bearing = journey.Value(xmlNamespace, "Bearing").ParseDecimal() ?? 0;

                    if (latitude is null || longitude is null)
                        continue;

                    string originDepartureKey = BusTimeTableExtension.CreateOriginDepartureKey(originAimedDepartureTime.Value, originRef, destinationRef);
                    busLocations[originDepartureKey] = new BusLocation
                    {
                        OriginDepartureKey = originDepartureKey,
                        RecordedAtTime = recordedAtTime.Value,

                        OperatorRef = operatorRef,
                        PublishedLineName = publishedLineName,
                        DirectionRef = directionRef,

                        OriginName = originName,
                        OriginRef = originRef,
                        OriginAimedDepartureTime = originAimedDepartureTime.Value,

                        DestinationName = destinationName,
                        DestinationRef = destinationRef,
                        DestinationAimedArrivalTime = destinationAimedArrivalTime,

                        Latitude = latitude.Value,
                        Longitude = longitude.Value,
                        Bearing = bearing
                    };
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to import bus locations from {Activity}", activity.Value);
                }
            }

            return busLocations;
        }



        public static async Task<IReadOnlyList<BusTimetable>> ParseBusTimetable(this Stream stream, XNamespace xmlNamespace, DateTime now, ILogger logger, CancellationToken cancellationToken)
        {
            List<BusTimetable> busTimetables = [];
            XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
            XElement root = document.Root ?? throw new InvalidDataException("Empty TransXChange document.");

            // Operators section
            var operatordById = new Dictionary<string, (string NationalOperatorCode, string ShortName)>();
            XElement operators = root.Element(xmlNamespace + "Operators") ?? throw new InvalidDataException("<Operators> element not found.");

            foreach (XElement busOperator in operators.Elements())
            {
                string operatorId = busOperator.AttributeValue("id") ?? throw new InvalidDataException("<id> element not found.");
                string nationalOperatorCode = busOperator.Value(xmlNamespace, "NationalOperatorCode") ?? throw new InvalidDataException("<NationalOperatorCode> element not found.");
                string operatorShortName = busOperator.Value(xmlNamespace, "OperatorShortName") ?? throw new InvalidDataException("<OperatorShortName> element not found.");

                // sometimes <id> in operator does not match with <OperatorRef> in <vehicleJourney>
                // the solution will be applying both operatorId and nationalOperatorCode to hope for at least one match
                operatordById[operatorId] = (nationalOperatorCode, operatorShortName);
                operatordById[nationalOperatorCode] = (nationalOperatorCode, operatorShortName);

                string? operatorCode = busOperator.Value(xmlNamespace, "OperatorCode");
                if (operatorCode is not null)
                    operatordById[operatorCode] = (nationalOperatorCode, operatorShortName);
            }


            // Line Section, Operating Profile Section and Journey Section
            var lineById = new Dictionary<string, string>();
            var serviceById = new Dictionary<string, (DateOnly StartDate, DateOnly EndDate, XElement? OperatingProfile)>();
            var journeyById = new Dictionary<string, (IReadOnlyList<string> SectionId, string direction, string origin, string destination, XElement? OperatingProfile)>();

            IEnumerable<XElement> services = root.Element(xmlNamespace + "Services")?.Elements(xmlNamespace + "Service") ?? throw new InvalidDataException("<Services> element not found.");

            foreach (XElement service in services)
            {
                // Get line name
                XElement lines = service.Element(xmlNamespace + "Lines") ?? throw new InvalidDataException("<Lines> element not found.");
                foreach (XElement line in lines.Elements(xmlNamespace + "Line"))
                {
                    string lineId = line.AttributeValue("id") ?? throw new InvalidDataException("Line 'id' attribute not found.");
                    string lineName = line.Value(xmlNamespace, "LineName") ?? throw new InvalidDataException("<LineName> element not found.");

                    lineById[lineId] = lineName;
                }

                // Get operating period and profile (Start date and End date etc)
                string serviceCode = service.Value(xmlNamespace, "ServiceCode") ?? throw new InvalidDataException("<ServiceCode> element not found.");
                XElement? period = service.Element(xmlNamespace + "OperatingPeriod");
                DateOnly startDate = period.Value(xmlNamespace, "StartDate").ParseDateOnly() ?? throw new InvalidDataException("<StartDate> element not found.");

                // some does not provide a end date, and even the end date can be before now, if it continue early then the journey cannot be added to timetable
                DateOnly endDate = period.Value(xmlNamespace, "EndDate").ParseDateOnly() ?? DateOnly.FromDateTime(now.AddDays(180));

                // The profile is carried unparsed because a profile in vehicle journey may replace it
                serviceById[serviceCode] = (startDate, endDate, service.Element(xmlNamespace + "OperatingProfile"));


                // Get the journey
                XElement standardService = service.Element(xmlNamespace + "StandardService") ?? throw new InvalidDataException("<StandardService> element not found.");

                string origin = standardService.Value(xmlNamespace, "Origin") ?? throw new InvalidDataException("<Origin> element not found.");
                string destination = standardService.Value(xmlNamespace, "Destination") ?? throw new InvalidDataException("<Destination> element not found.");

                foreach (XElement journeyPattern in standardService.Elements(xmlNamespace + "JourneyPattern"))
                {
                    string journeyId = journeyPattern.AttributeValue("id") ?? throw new InvalidDataException("JourneyPattern 'id' attribute not found.");
                    string direction = journeyPattern.Value(xmlNamespace, "Direction") ?? throw new InvalidDataException("<Direction> element not found.");

                    List<string> sectionIds = journeyPattern
                        .Elements(xmlNamespace + "JourneyPatternSectionRefs")
                        .Select(r => r.Value.Trim())
                        .ToList();

                    journeyById[journeyId] = (sectionIds, direction, origin, destination, journeyPattern.Element(xmlNamespace + "OperatingProfile"));
                }
            }



            // Journey Pattern Section
            var stopsBySectionId = new Dictionary<string, List<(string StopId, int Sequence, double MinutesFromDeparture)>>();
            XElement journeyPatternSections = root.Element(xmlNamespace + "JourneyPatternSections") ?? throw new InvalidDataException("<JourneyPatternSections> element not found.");

            foreach (XElement journeyPatternSection in journeyPatternSections.Elements(xmlNamespace + "JourneyPatternSection"))
            {
                string sectionId = journeyPatternSection.AttributeValue("id") ?? throw new InvalidDataException("JourneyPatternSection 'id' attribute not found.");

                var stops = new List<(string StopId, int Sequence, double MinutesFromDeparture)>();

                // Minutes are measured from the departure at the first stop of the section.
                TimeSpan offsetFromDeparture = TimeSpan.Zero;
                int sequence = 1;

                List<XElement> timingLinks = journeyPatternSection.Elements(xmlNamespace + "JourneyPatternTimingLink").ToList();
                if (timingLinks.Count == 0)
                    throw new InvalidDataException("<JourneyPatternTimingLink> element not found");

                // Every link's To is the next link's From, so take the From of each link...
                foreach (XElement timingLink in timingLinks)
                {
                    XElement from = timingLink.Element(xmlNamespace + "From") ?? throw new InvalidDataException("<From> element not found.");
                    string fromStopId = from.Value(xmlNamespace, "StopPointRef") ?? throw new InvalidDataException("<StopPointRef> element not found.");

                    stops.Add((fromStopId, sequence, offsetFromDeparture.TotalMinutes));

                    // A wait at the current stop before moving to the next stop.
                    offsetFromDeparture += from.Value(xmlNamespace, "WaitTime").ParseDuration();
                    offsetFromDeparture += timingLink.Value(xmlNamespace, "RunTime").ParseDuration();
                    sequence++;
                }

                XElement lastTo = timingLinks[timingLinks.Count - 1].Element(xmlNamespace + "To") ?? throw new InvalidDataException("<To> element not found.");
                string lastStopId = lastTo.Value(xmlNamespace, "StopPointRef") ?? throw new InvalidDataException("<StopPointRef> element not found.");

                stops.Add((lastStopId, sequence, offsetFromDeparture.TotalMinutes));

                stopsBySectionId[sectionId] = stops;
            }


            // Vehicle Journey Section
            XElement vehicleJourneys = root.Element(xmlNamespace + "VehicleJourneys") ?? throw new InvalidDataException("<VehicleJourneys> element not found.");

            // A service- or pattern-level element is shared by many journeys, so each distinct element is parsed once.
            var profileByElement = new Dictionary<XElement, (HashSet<DayOfWeek> Days, BankHoliday BankHolidaysOfOperation, BankHoliday BankHolidaysOfNonOperation)>();

            foreach (XElement vehicleJourney in vehicleJourneys.Elements(xmlNamespace + "VehicleJourney"))
            {
                string operatorId = vehicleJourney.Value(xmlNamespace, "OperatorRef") ?? throw new InvalidDataException("<OperatorRef> element not found.");
                if (!operatordById.TryGetValue(operatorId, out (string NationalOperatorCode, string ShortName) busOperator))
                    throw new InvalidDataException($"Operator Id not found {operatorId}");

                string journeyId = vehicleJourney.Value(xmlNamespace, "JourneyPatternRef") ?? throw new InvalidDataException("<JourneyPatternRef> element not found.");
                if (!journeyById.TryGetValue(journeyId, out (IReadOnlyList<string> SectionIds, string direction, string origin, string destination, XElement? OperatingProfile) journey))
                    throw new InvalidDataException($"Journey Id not found {journeyId}");

                string lineId = vehicleJourney.Value(xmlNamespace, "LineRef") ?? throw new InvalidDataException("<LineRef> element not found.");
                if (!lineById.TryGetValue(lineId, out string? lineName) || lineName == null)
                    throw new InvalidDataException($"Line Id not found {lineId}");

                string serviceId = vehicleJourney.Value(xmlNamespace, "ServiceRef") ?? throw new InvalidDataException("<ServiceRef> element not found.");
                if (!serviceById.TryGetValue(serviceId, out (DateOnly StartDate, DateOnly EndDate, XElement? OperatingProfile) service))
                    throw new InvalidDataException($"Service Id not found {serviceId}");

                if (DateOnly.FromDateTime(now) > service.EndDate)
                    continue;

                // TransXChange resolves the operating profile most-specific-first: a vehicle journey replaces the journey patterns, which replaces the service's
                XElement? operatingProfile = vehicleJourney.Element(xmlNamespace + "OperatingProfile")
                    ?? journey.OperatingProfile
                    ?? service.OperatingProfile;

                (HashSet<DayOfWeek> Days, BankHoliday BankHolidaysOfOperation, BankHoliday BankHolidaysOfNonOperation) profile = ResolveOperatingProfile(operatingProfile, xmlNamespace, profileByElement);

                string departure = vehicleJourney.Value(xmlNamespace, "DepartureTime") ?? throw new InvalidDataException("<DepartureTime> element not found.");
                TimeOnly departureTime = TimeOnly.Parse(departure, CultureInfo.InvariantCulture);

                string timetableId = Guid.NewGuid().ToString();
                List<BusCallingPoint> busCallingPoints = [];

                // The pattern's sections stack on top of each other, each picking up where the previous one ended.
                int sequenceOffset = 0;
                double minutesOffset = 0;

                // the nested for loop in section ids is written because a <JourneyPattern> can have multiple <JourneyPatternSectionRefs>
                foreach (string sectionId in journey.SectionIds)
                {
                    if (!stopsBySectionId.TryGetValue(sectionId, out List<(string StopId, int Sequence, double MinutesFromDeparture)>? stops))
                        throw new InvalidDataException($"Section Id not found {sectionId}");

                    foreach ((string stopId, int sequence, double minutesFromDeparture) in stops)
                    {
                        // A section's first stop is the previous section's last stop.
                        if (busCallingPoints.Count > 0 && sequence == 1)
                            continue;

                        // A journey crossing midnight wraps the clock, so keep the day it lands on.
                        TimeOnly scheduledTime = departureTime.AddMinutes(minutesFromDeparture + minutesOffset, out int scheduledDayOffset);

                        busCallingPoints.Add(new BusCallingPoint()
                        {
                            BusTimetableId = timetableId,
                            Sequence = sequence + sequenceOffset,
                            BusStopId = stopId,
                            LineName = lineName,
                            OperatorRef = busOperator.NationalOperatorCode,
                            ScheduledTime = scheduledTime,
                            ScheduledDayOffset = scheduledDayOffset
                        });
                    }

                    // The shared stop counts once, so the next section starts one short of the total.
                    (string StopId, int Sequence, double MinutesFromDeparture) lastStop = stops[stops.Count - 1];
                    sequenceOffset += lastStop.Sequence - 1;
                    minutesOffset += lastStop.MinutesFromDeparture;
                }

                if (busCallingPoints.Count <= 1)
                    throw new InvalidDataException($"Only have {busCallingPoints.Count} calling points");

                BusCallingPoint firstCallingPoint = busCallingPoints[0];
                BusCallingPoint lastCallingPoint = busCallingPoints[busCallingPoints.Count - 1];

                busTimetables.Add(new BusTimetable()
                {
                    Id = timetableId,
                    OperatorRef = operatorId,
                    LineName = lineName,
                    OriginName = journey.origin,
                    DestinationName = journey.destination,
                    Direction = journey.direction,
                    ScheduledDayOffset = lastCallingPoint.ScheduledDayOffset,
                    StartDate = service.StartDate,
                    EndDate = service.EndDate,
                    OriginDepartureKey = BusTimeTableExtension.CreateOriginDepartureKey(departureTime, firstCallingPoint.BusStopId, lastCallingPoint.BusStopId),
                    Monday = profile.Days.Contains(DayOfWeek.Monday),
                    Tuesday = profile.Days.Contains(DayOfWeek.Tuesday),
                    Wednesday = profile.Days.Contains(DayOfWeek.Wednesday),
                    Thursday = profile.Days.Contains(DayOfWeek.Thursday),
                    Friday = profile.Days.Contains(DayOfWeek.Friday),
                    Saturday = profile.Days.Contains(DayOfWeek.Saturday),
                    Sunday = profile.Days.Contains(DayOfWeek.Sunday),
                    BankHolidaysOfOperation = profile.BankHolidaysOfOperation,
                    BankHolidaysOfNonOperation = profile.BankHolidaysOfNonOperation,
                    BusCallingPoints = busCallingPoints,
                });
            }

            return busTimetables;
        }

        /// <summary>
        /// Parses the days and bank holiday rules out of a single resolved &lt;OperatingProfile&gt;, reusing the
        /// result when the same element is shared by several vehicle journeys. A null element means the journey
        /// carries no profile at any level, which leaves it running on no day at all.
        /// </summary>
        private static (HashSet<DayOfWeek> Days, BankHoliday BankHolidaysOfOperation, BankHoliday BankHolidaysOfNonOperation) ResolveOperatingProfile(
            XElement? operatingProfile,
            XNamespace xmlNamespace,
            Dictionary<XElement, (HashSet<DayOfWeek> Days, BankHoliday BankHolidaysOfOperation, BankHoliday BankHolidaysOfNonOperation)> profileByElement)
        {
            if (operatingProfile is null)
                throw new InvalidDataException("<OperatingProfile> element not found.");

            if (!profileByElement.TryGetValue(operatingProfile, out (HashSet<DayOfWeek> Days, BankHoliday BankHolidaysOfOperation, BankHoliday BankHolidaysOfNonOperation) profile))
            {
                profile = (ParseDaysOfWeek(operatingProfile, xmlNamespace),
                    ParseBankHolidays(operatingProfile, "DaysOfOperation", xmlNamespace),
                    ParseBankHolidays(operatingProfile, "DaysOfNonOperation", xmlNamespace));

                profileByElement[operatingProfile] = profile;
            }

            return profile;
        }


        private static HashSet<DayOfWeek> ParseDaysOfWeek(XElement? operatingProfile, XNamespace xmlNamespace)
        {
            var days = new HashSet<DayOfWeek>();

            XElement? regularDayType = operatingProfile?.Element(xmlNamespace + "RegularDayType");

            // <HolidaysOnly/> means the journey never runs on a regular weekday.
            if (regularDayType?.Element(xmlNamespace + "HolidaysOnly") is not null)
                return days;

            XElement? daysOfWeek = regularDayType?.Element(xmlNamespace + "DaysOfWeek");
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


        private static BankHoliday ParseBankHolidays(XElement? operatingProfile, string operation, XNamespace xmlNamespace)
        {
            const BankHoliday christmasDays = BankHoliday.ChristmasDay | BankHoliday.BoxingDay;
            const BankHoliday otherBankHolidayDays = BankHoliday.GoodFriday | BankHoliday.NewYearsDay | BankHoliday.Jan2ndScotland | BankHoliday.StAndrewsDay;
            const BankHoliday holidayMondays = BankHoliday.LateSummerBankHolidayNotScotland | BankHoliday.MayDay | BankHoliday.EasterMonday | BankHoliday.SpringBank | BankHoliday.AugustBankHolidayScotland;
            const BankHoliday displacementHolidays = BankHoliday.ChristmasDayHoliday | BankHoliday.BoxingDayHoliday | BankHoliday.NewYearsDayHoliday | BankHoliday.Jan2ndScotlandHoliday | BankHoliday.StAndrewsDayHoliday;
            const BankHoliday earlyRunOffDays = BankHoliday.ChristmasEve | BankHoliday.NewYearsEve;

            BankHoliday bankHolidays = BankHoliday.None;

            XElement? days = operatingProfile?.Element(xmlNamespace + "BankHolidayOperation")?.Element(xmlNamespace + operation);
            if (days is null)
                return bankHolidays;

            foreach (XElement day in days.Elements())
            {
                switch (day.Name.LocalName)
                {
                    // Umbrella tags standing in for a whole group of days.
                    case "AllBankHolidays": bankHolidays |= christmasDays | otherBankHolidayDays | holidayMondays | displacementHolidays; break;
                    case "Christmas": bankHolidays |= christmasDays; break;
                    case "AllHolidaysExceptChristmas": bankHolidays |= otherBankHolidayDays | holidayMondays; break;
                    case "HolidayMondays": bankHolidays |= holidayMondays; break;
                    case "DisplacementHolidays": bankHolidays |= displacementHolidays; break;
                    case "EarlyRunOffDays": bankHolidays |= earlyRunOffDays; break;

                    // ToDo, just skip holiday section for now there are way more holiday then default like 
                    // <OtherPublicHoliday> is not supported: CoronationOfKingCharlesIII
                    // <OtherPublicHoliday> is not a known bank holiday value.

                    /*
                    // Carries a Description and an optional Date, so it has no place in a fixed enum.
                    case "OtherPublicHoliday":
                        throw new InvalidDataException($"<OtherPublicHoliday> is not supported: {day.Value(xmlNamespace, "Description")}");

                    // The parse is the validation: anything outside the schema vocabulary fails here.
                    default:
                        if (!Enum.TryParse(day.Name.LocalName, out BankHoliday holiday))
                            throw new InvalidDataException($"<{day.Name.LocalName}> is not a known bank holiday value.");

                        bankHolidays |= holiday;
                        break;*/
                }
            }

            return bankHolidays;
        }


        private static IEnumerable<DayOfWeek> AllDaysExcept(params DayOfWeek[] excluded)
        {
            return Enum.GetValues<DayOfWeek>().Where(d => !excluded.Contains(d));
        }
    }
}
