using Backend.Enumerations;
using Backend.Models;
using System.Text;
using System.Xml.Linq;

namespace Backend.Extensions
{
    public static class ParserExtension
    {
        // The holidays each TransXChange umbrella tag stands for. Only the umbrella tags need listing, since every other
        // tag is stored under its own name.
        private static readonly string[] s_christmasDays = ["ChristmasDay", "BoxingDay"];
        private static readonly string[] s_otherBankHolidayDays = ["GoodFriday", "NewYearsDay", "Jan2ndScotland", "StAndrewsDay"];
        private static readonly string[] s_holidayMondays = ["LateSummerBankHolidayNotScotland", "MayDay", "EasterMonday", "SpringBank", "AugustBankHolidayScotland"];
        private static readonly string[] s_displacementHolidays = ["ChristmasDayHoliday", "BoxingDayHoliday", "NewYearsDayHoliday", "Jan2ndScotlandHoliday", "StAndrewsDayHoliday"];
        private static readonly string[] s_earlyRunOffDays = ["ChristmasEve", "NewYearsEve"];

        public static async Task<Dictionary<string, BusLocation>> ParseBusLocation(this Stream stream, XNamespace xmlNamespace, DateTime now, TimeSpan retentionPeriod, TimeZoneInfo timeZoneInfo, ILogger logger, CancellationToken cancellationToken)
        {
            Dictionary<string, BusLocation> busLocations = [];
            XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            // Counted rather than logged one by one, since a feed carries twenty thousand vehicles and is read every
            // ten seconds. The totals say whether a source is publishing badly; a line per vehicle would not.
            int vehicleCount = 0;
            int expiredCount = 0;
            int missingLineNameCount = 0;
            int missingJourneyCount = 0;
            int missingCoordinateCount = 0;

            IEnumerable<XElement> activities = document.Descendants(xmlNamespace + "VehicleActivity");
            foreach (XElement activity in activities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    vehicleCount++;

                    DateTime? recordedAtTime = activity.Value(xmlNamespace, "RecordedAtTime").ParseUkDateTime(timeZoneInfo);
                    if (recordedAtTime is null || now - retentionPeriod > recordedAtTime.Value)
                    {
                        expiredCount++;
                        continue;
                    }

                    XElement journey = activity.Element(xmlNamespace + "MonitoredVehicleJourney") ?? throw new InvalidDataException("<MonitoredVehicleJourney> element not found.");

                    // string? operatorRef = journey.Value(xmlNamespace, "OperatorRef");
                    string? lineName = journey.Value(xmlNamespace, "PublishedLineName");   
                    if (lineName is null)
                    {
                        missingLineNameCount++;
                        continue;
                    }


                    string? originBusStopId = journey.Value(xmlNamespace, "OriginRef");
                    string? destinationBusStopId = journey.Value(xmlNamespace, "DestinationRef");    
                    // string? direction = journey.Value(xmlNamespace, "DirectionRef"); // inbound, outbound, clockwise, anticlockwise, 1, 2

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
                    if (originBusStopId is null || destinationBusStopId is null || !originAimedDepartureTime.HasValue)
                    {
                        missingJourneyCount++;
                        continue;
                    }

                    XElement? location = journey?.Element(xmlNamespace + "VehicleLocation");
                    decimal? latitude = location.Value(xmlNamespace, "Latitude").ParseDecimal();
                    decimal? longitude = location.Value(xmlNamespace, "Longitude").ParseDecimal();
                    decimal bearing = journey.Value(xmlNamespace, "Bearing").ParseDecimal() ?? 0;

                    if (latitude is null || longitude is null)
                    {
                        missingCoordinateCount++;
                        continue;
                    }

                    string tripScheduleKey = BusTimeTableExtension.BuildJourneyKey(lineName, originAimedDepartureTime.Value, originBusStopId, destinationBusStopId);
                    busLocations[tripScheduleKey] = new BusLocation
                    {
                        JourneyKey = tripScheduleKey,
                        RecordedAtTime = recordedAtTime.Value,

                        LineName = lineName.ToUpperInvariant(),

                        OriginBusStopId = originBusStopId,
                        OriginAimedDepartureTime = originAimedDepartureTime.Value,

                        DestinationBusStopId = destinationBusStopId,
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

            int skippedCount = expiredCount + missingLineNameCount + missingJourneyCount + missingCoordinateCount;
            logger.LogInformation(
                "Bus location - {Valid} valid, {Skipped} skipped ({Expired} expired, {MissingLineName} no line name, {MissingJourney} no journey, {MissingLocation} no coordinates)",
                busLocations.Count, skippedCount, expiredCount, missingLineNameCount, missingJourneyCount, missingCoordinateCount);

            return busLocations;
        }



        public static async Task<IReadOnlyList<BusTimetable>> ParseBusTimetable(this Stream stream, XNamespace xmlNamespace, string datasetId, DateTime now, ILogger logger, CancellationToken cancellationToken)
        {
            List<BusTimetable> busTimetables = [];
            XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
            XElement root = document.Root ?? throw new InvalidDataException("Empty TransXChange document.");

            // Operators section
            var operatordById = new Dictionary<string, (string OperatorId, string Name)>();
            XElement operators = root.Element(xmlNamespace + "Operators") ?? throw new InvalidDataException("<Operators> element not found.");

            foreach (XElement busOperator in operators.Elements())
            {
                string operatorId = busOperator.AttributeValue("id") ?? throw new InvalidDataException("<id> element not found.");
                string nationalOperatorCode = busOperator.Value(xmlNamespace, "NationalOperatorCode")
                    ?? busOperator.Value(xmlNamespace, "OperatorCode")
                    ?? throw new InvalidDataException("Neither <NationalOperatorCode> nor <OperatorCode> element found.");

                string operatorShortName = busOperator.Value(xmlNamespace, "OperatorShortName") ?? throw new InvalidDataException("<OperatorShortName> element not found.");

                // sometimes <id> in operator does not match with <OperatorRef> in <vehicleJourney>
                // the solution will be applying both operatorId and nationalOperatorCode to hope for at least one match
                operatordById[operatorId] = (nationalOperatorCode, operatorShortName);
                operatordById[nationalOperatorCode] = (nationalOperatorCode,  operatorShortName);
            }


            // Line Section, Operating Profile Section and Journey Section
            var lineById = new Dictionary<string, string>();
            var serviceById = new Dictionary<string, (DateOnly StartDate, DateOnly EndDate, XElement? OperatingProfile, string? RegisteredOperatorRef)>();
            var journeyById = new Dictionary<string, (IReadOnlyList<string> SectionId, string direction, XElement? OperatingProfile)>();

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
                // The registered operator is carried too, standing in for any journey that does not name its own
                serviceById[serviceCode] = (startDate, endDate, service.Element(xmlNamespace + "OperatingProfile"), service.Value(xmlNamespace, "RegisteredOperatorRef"));


                // Get the journey
                XElement standardService = service.Element(xmlNamespace + "StandardService") ?? throw new InvalidDataException("<StandardService> element not found.");

                foreach (XElement journeyPattern in standardService.Elements(xmlNamespace + "JourneyPattern"))
                {
                    string journeyId = journeyPattern.AttributeValue("id") ?? throw new InvalidDataException("JourneyPattern 'id' attribute not found.");
                    string direction = journeyPattern.Value(xmlNamespace, "Direction") ?? throw new InvalidDataException("<Direction> element not found.");

                    List<string> sectionIds = journeyPattern
                        .Elements(xmlNamespace + "JourneyPatternSectionRefs")
                        .Select(r => r.Value.Trim())
                        .ToList();

                    journeyById[journeyId] = (sectionIds, direction, journeyPattern.Element(xmlNamespace + "OperatingProfile"));
                }
            }



            // Journey Pattern Section
            var stopsBySectionId = new Dictionary<string, List<SectionStop>>();
            XElement journeyPatternSections = root.Element(xmlNamespace + "JourneyPatternSections") ?? throw new InvalidDataException("<JourneyPatternSections> element not found.");

            foreach (XElement journeyPatternSection in journeyPatternSections.Elements(xmlNamespace + "JourneyPatternSection"))
            {
                string sectionId = journeyPatternSection.AttributeValue("id") ?? throw new InvalidDataException("JourneyPatternSection 'id' attribute not found.");

                var stops = new List<SectionStop>();
                int sequence = 1;

                List<XElement> timingLinks = journeyPatternSection.Elements(xmlNamespace + "JourneyPatternTimingLink").ToList();
                if (timingLinks.Count == 0)
                    throw new InvalidDataException("<JourneyPatternTimingLink> element not found");

                // Every link's To is the next link's From, so take the From of each link...
                for (int i = 0; i < timingLinks.Count; i++)
                {
                    XElement timingLink = timingLinks[i];
                    string timingLinkId = timingLink.AttributeValue("id") ?? string.Empty;

                    XElement from = timingLink.Element(xmlNamespace + "From") ?? throw new InvalidDataException("<From> element not found.");
                    string fromStopId = from.Value(xmlNamespace, "StopPointRef") ?? throw new InvalidDataException("<StopPointRef> element not found.");

                    // ...and the wait at that stop can be written on either side of it, since it is this link's From and
                    // the previous link's To. Publishers pick one or the other, so both are read.
                    string? waitTime = from.Value(xmlNamespace, "WaitTime")
                        ?? (i == 0 ? null : timingLinks[i - 1].Element(xmlNamespace + "To").Value(xmlNamespace, "WaitTime"));

                    // The wait is at this stop before moving on, the run is the leg to the next one.
                    stops.Add(new SectionStop(
                        timingLinkId,
                        fromStopId,
                        sequence,
                        IsPassedStop(from, xmlNamespace),
                        waitTime.ParseDuration(),
                        timingLink.Value(xmlNamespace, "RunTime").ParseDuration()));

                    sequence++;
                }

                XElement lastTimingLink = timingLinks[timingLinks.Count - 1];
                XElement lastTo = lastTimingLink.Element(xmlNamespace + "To") ?? throw new InvalidDataException("<To> element not found.");
                string lastStopId = lastTo.Value(xmlNamespace, "StopPointRef") ?? throw new InvalidDataException("<StopPointRef> element not found.");

                // Nothing runs on from the last stop, so it carries no times of its own.
                stops.Add(new SectionStop(
                    lastTimingLink.AttributeValue("id") ?? string.Empty,
                    lastStopId,
                    sequence,
                    IsPassedStop(lastTo, xmlNamespace),
                    TimeSpan.Zero,
                    TimeSpan.Zero));

                stopsBySectionId[sectionId] = stops;
            }


            // Vehicle Journey Section
            XElement vehicleJourneys = root.Element(xmlNamespace + "VehicleJourneys") ?? throw new InvalidDataException("<VehicleJourneys> element not found.");

            // A service- or pattern-level element is shared by many journeys, so each distinct element is parsed once.
            var daysByElement = new Dictionary<XElement, HashSet<DayOfWeek>>();
            var publicHolidaysByElement = new Dictionary<XElement, IReadOnlyList<string>>();
            var dateRangesByElement = new Dictionary<XElement, IReadOnlyList<(DateOnly StartDate, DateOnly EndDate)>>();
            var weeksByElement = new Dictionary<XElement, WeekOfMonth>();

            // Falling back to the default days is worth knowing about, but only once for the whole document.
            bool warnedMissingRegularDayType = false;

            foreach (XElement vehicleJourney in vehicleJourneys.Elements(xmlNamespace + "VehicleJourney"))
            {
                string serviceId = vehicleJourney.Value(xmlNamespace, "ServiceRef") ?? throw new InvalidDataException("<ServiceRef> element not found.");
                if (!serviceById.TryGetValue(serviceId, out (DateOnly StartDate, DateOnly EndDate, XElement? OperatingProfile, string? RegisteredOperatorRef) service))
                    throw new InvalidDataException($"Service Id not found {serviceId}");

                string operatorId = vehicleJourney.Value(xmlNamespace, "OperatorRef")
                    ?? service.RegisteredOperatorRef
                    ?? throw new InvalidDataException("<OperatorRef> element not found.");

                if (!operatordById.TryGetValue(operatorId, out (string OperatorId, string Name) busOperator))
                    throw new InvalidDataException($"Operator Id not found {operatorId}");

                string journeyId = vehicleJourney.Value(xmlNamespace, "JourneyPatternRef") ?? throw new InvalidDataException("<JourneyPatternRef> element not found.");
                if (!journeyById.TryGetValue(journeyId, out (IReadOnlyList<string> SectionIds, string direction, XElement? OperatingProfile) journey))
                    throw new InvalidDataException($"Journey Id not found {journeyId}");

                string lineId = vehicleJourney.Value(xmlNamespace, "LineRef") ?? throw new InvalidDataException("<LineRef> element not found.");
                if (!lineById.TryGetValue(lineId, out string? lineName) || lineName == null)
                    throw new InvalidDataException($"Line Id not found {lineId}");

                if (DateOnly.FromDateTime(now) > service.EndDate)
                    continue;

                // TransXChange resolves each profile property on its own, most-specific-first: the lowest level that states a property replaces it outright,
                // and a level that omits one inherits it from above instead of blanking it. 
                var profileLevels = new OperatingProfileLevels(
                    vehicleJourney.Element(xmlNamespace + "OperatingProfile"),
                    journey.OperatingProfile,
                    service.OperatingProfile);

                XElement? regularDayType = profileLevels.Resolve(xmlNamespace, "RegularDayType");
                XElement? holidaysOfOperation = profileLevels.Resolve(xmlNamespace, "BankHolidayOperation", "DaysOfOperation");
                XElement? holidaysOfNonOperation = profileLevels.Resolve(xmlNamespace, "BankHolidayOperation", "DaysOfNonOperation");
                XElement? specialDaysOfOperation = profileLevels.Resolve(xmlNamespace, "SpecialDaysOperation", "DaysOfOperation");
                XElement? specialDaysOfNonOperation = profileLevels.Resolve(xmlNamespace, "SpecialDaysOperation", "DaysOfNonOperation");
                XElement? periodicDayType = profileLevels.Resolve(xmlNamespace, "PeriodicDayType");

                if (regularDayType is null && !warnedMissingRegularDayType)
                {
                    logger.LogWarning("No <RegularDayType> at any level, defaulting journeys to Monday to Friday.");
                    warnedMissingRegularDayType = true;
                }

                HashSet<DayOfWeek> days = ResolveDaysOfWeek(regularDayType, xmlNamespace, daysByElement);
                WeekOfMonth weeksOfMonth = ResolveWeeksOfMonth(periodicDayType, xmlNamespace, logger, weeksByElement);
                IReadOnlyList<string> publicHolidaysOfOperation = ResolvePublicHolidays(holidaysOfOperation, xmlNamespace, logger, publicHolidaysByElement);
                IReadOnlyList<string> publicHolidaysOfNonOperation = ResolvePublicHolidays(holidaysOfNonOperation, xmlNamespace, logger, publicHolidaysByElement);
                IReadOnlyList<(DateOnly StartDate, DateOnly EndDate)> operatingDates = ResolveDateRanges(specialDaysOfOperation, xmlNamespace, dateRangesByElement);
                IReadOnlyList<(DateOnly StartDate, DateOnly EndDate)> nonOperatingDates = ResolveDateRanges(specialDaysOfNonOperation, xmlNamespace, dateRangesByElement);

                string departure = vehicleJourney.Value(xmlNamespace, "DepartureTime") ?? throw new InvalidDataException("<DepartureTime> element not found.");

                // A journey leaving after midnight is stated as 24:00 or later against the operating day it belongs to,
                // so the hour is folded back to a wall clock and the day it carried is kept as the journey's own offset.
                TimeOnly departureTime = departure.ParseTimeOnlyWithDayOffset(out int departureDayOffset)
                    ?? throw new InvalidDataException($"<DepartureTime> is not a time: {departure}");

                string timetableId = Guid.NewGuid().ToString();
                List<BusCallingPoint> busCallingPoints = [];

                List<BusSpecialDay> busSpecialDays =
                [
                    .. operatingDates.Select(x => new BusSpecialDay
                    {
                        BusTimetableId = timetableId,
                        StartDate = x.StartDate,
                        EndDate = x.EndDate,
                        IsOperating = true
                    }),
                    .. nonOperatingDates.Select(x => new BusSpecialDay
                    {
                        BusTimetableId = timetableId,
                        StartDate = x.StartDate,
                        EndDate = x.EndDate,
                        IsOperating = false
                    }),
                ];

                List<BusHoliday> busHolidays =
                [
                    .. publicHolidaysOfOperation.Select(x => new BusHoliday
                    {
                        BusTimetableId = timetableId,
                        PublicHolidayName = x,
                        IsOperating = true
                    }),
                    .. publicHolidaysOfNonOperation.Select(x => new BusHoliday
                    {
                        BusTimetableId = timetableId,
                        PublicHolidayName = x,
                        IsOperating = false
                    }),
                ];

                var journeyWaitTimeByTimingLinkId = new Dictionary<string, TimeSpan>();
                var journeyRunTimeByTimingLinkId = new Dictionary<string, TimeSpan>();

                foreach (XElement journeyTimingLink in vehicleJourney.Elements(xmlNamespace + "VehicleJourneyTimingLink"))
                {
                    string? timingLinkId = journeyTimingLink.Value(xmlNamespace, "JourneyPatternTimingLinkRef");
                    if (timingLinkId is null)
                        continue;

                    XElement? journeyFrom = journeyTimingLink.Element(xmlNamespace + "From");
                    string? waitTime = journeyFrom.Value(xmlNamespace, "WaitTime");
                    if (waitTime is not null)
                        journeyWaitTimeByTimingLinkId[timingLinkId] = waitTime.ParseDuration();

                    string? runTime = journeyTimingLink.Value(xmlNamespace, "RunTime");
                    if (runTime is not null)
                        journeyRunTimeByTimingLinkId[timingLinkId] = runTime.ParseDuration();
                }

                // The pattern's sections stack on top of each other, each picking up where the previous one ended.
                bool isFirstSection = true;
                TimeSpan offsetFromDeparture = TimeSpan.Zero;

                // the nested for loop in section ids is written because a <JourneyPattern> can have multiple <JourneyPatternSectionRefs>
                foreach (string sectionId in journey.SectionIds)
                {
                    if (!stopsBySectionId.TryGetValue(sectionId, out List<SectionStop>? stops))
                        throw new InvalidDataException($"Section Id not found {sectionId}");

                    foreach (SectionStop stop in stops)
                    {
                        // A section's first stop is the previous section's last stop, so it is not called at twice. Its
                        // times still count though, since the leg on from it belongs to this section, not the last one.
                        bool isRepeatedSectionStop = !isFirstSection && stop.Sequence == 1;

                        // The journey may already have started a day on from its operating day, so the days this stop
                        // wraps are counted on top of the ones the departure itself carried.
                        TimeOnly scheduledTime = departureTime.AddMinutes(offsetFromDeparture.TotalMinutes, out int wrappedDays);
                        int scheduledDayOffset = departureDayOffset + wrappedDays;

                        if (!stop.IsPassed && !isRepeatedSectionStop)
                        {
                            busCallingPoints.Add(new BusCallingPoint()
                            {
                                BusTimetableId = timetableId,
                                Sequence = busCallingPoints.Count + 1,
                                BusStopId = stop.StopId,
                                //LineName = lineName,
                                //OperatorId = busOperator.OperatorId,
                                ScheduledTime = scheduledTime,
                                ScheduledDayOffset = scheduledDayOffset
                            });
                        }

                        // Advanced even for a passed stop, since the bus still takes that long to drive it.
                        offsetFromDeparture += journeyWaitTimeByTimingLinkId.TryGetValue(stop.TimingLinkId, out TimeSpan journeyWaitTime)
                            ? journeyWaitTime
                            : stop.WaitTime;

                        offsetFromDeparture += journeyRunTimeByTimingLinkId.TryGetValue(stop.TimingLinkId, out TimeSpan journeyRunTime)
                            ? journeyRunTime
                            : stop.RunTime;
                    }

                    isFirstSection = false;
                }

                if (busCallingPoints.Count <= 1)
                    throw new InvalidDataException($"Only have {busCallingPoints.Count} calling points");

                BusCallingPoint firstCallingPoint = busCallingPoints[0];
                BusCallingPoint lastCallingPoint = busCallingPoints[busCallingPoints.Count - 1];

                busTimetables.Add(new BusTimetable()
                {
                    Id = timetableId,
                    DatasetId = datasetId,
                    OperatorId = busOperator.OperatorId,
                    OperatorName = busOperator.Name,
                    LineName = lineName.ToUpperInvariant(),
                    DepartureTime = firstCallingPoint.ScheduledTime,
                    OriginBusStopId = firstCallingPoint.BusStopId,
                    ArrivalTime = lastCallingPoint.ScheduledTime,
                    DestinationBusStopId = lastCallingPoint.BusStopId,
                    Direction = journey.direction,
                    DepartureDayOffset = firstCallingPoint.ScheduledDayOffset,
                    ArrivalDayOffset = lastCallingPoint.ScheduledDayOffset,
                    StartDate = service.StartDate,
                    EndDate = service.EndDate,
                    JourneyKey = BusTimeTableExtension.BuildJourneyKey(lineName, departureTime, firstCallingPoint.BusStopId, lastCallingPoint.BusStopId),
                    RouteKey = BusTimeTableExtension.BuildRouteKey(lineName, firstCallingPoint.BusStopId, lastCallingPoint.BusStopId),
                    WeeksOfMonth = weeksOfMonth,
                    Monday = days.Contains(DayOfWeek.Monday),
                    Tuesday = days.Contains(DayOfWeek.Tuesday),
                    Wednesday = days.Contains(DayOfWeek.Wednesday),
                    Thursday = days.Contains(DayOfWeek.Thursday),
                    Friday = days.Contains(DayOfWeek.Friday),
                    Saturday = days.Contains(DayOfWeek.Saturday),
                    Sunday = days.Contains(DayOfWeek.Sunday),
                    BusCallingPoints = busCallingPoints,
                    BusSpecialDays = busSpecialDays,
                    BusHolidays = busHolidays,
                });
            }

            return busTimetables;
        }


        // The wait and run times are the pattern's own, kept per link rather than added up, because a vehicle journey
        // can override either and the same pattern is then timed differently for each journey using it.
        private sealed record SectionStop(string TimingLinkId, string StopId, int Sequence, bool IsPassed, TimeSpan WaitTime, TimeSpan RunTime);


        // <Activity> is optional and defaults to pickUpAndSetDown, so only an explicit "pass" drops the stop.
        private static bool IsPassedStop(XElement stopUsage, XNamespace xmlNamespace)
        {
            // Permitted values are pickUp, setDown, pickUpAndSetDown and pass.
            return string.Equals(stopUsage.Value(xmlNamespace, "Activity"), "pass", StringComparison.OrdinalIgnoreCase);
        }


        private sealed record OperatingProfileLevels(XElement? VehicleJourney, XElement? JourneyPattern, XElement? Service)
        {
            public XElement? Resolve(XNamespace xmlNamespace, params string[] path)
            {
                XElement? Find(XElement? operatingProfile)
                {
                    XElement? element = operatingProfile;
                    foreach (string name in path)
                    {
                        element = element?.Element(xmlNamespace + name);
                    }

                    return element;
                }

                return Find(VehicleJourney) ?? Find(JourneyPattern) ?? Find(Service);
            }
        }


        private static HashSet<DayOfWeek> ResolveDaysOfWeek(XElement? regularDayType, XNamespace xmlNamespace, Dictionary<XElement, HashSet<DayOfWeek>> daysByElement)
        {
            // No level states one, so the schema's own default stands in: Monday to Friday, every week of the year.
            if (regularDayType is null)
                return [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

            if (!daysByElement.TryGetValue(regularDayType, out HashSet<DayOfWeek>? days))
            {
                days = ParseDaysOfWeek(regularDayType, xmlNamespace);
                daysByElement[regularDayType] = days;
            }

            return days;
        }

        private static WeekOfMonth ResolveWeeksOfMonth(XElement? periodicDayType, XNamespace xmlNamespace, ILogger logger, Dictionary<XElement, WeekOfMonth> weeksByElement)
        {
            // can be null here for bus running some specific week on a month since majority of bus does not have this
            if (periodicDayType is null)
                return WeekOfMonth.None;

            if (!weeksByElement.TryGetValue(periodicDayType, out WeekOfMonth weeks))
            {
                weeks = ParseWeeksOfMonth(periodicDayType, xmlNamespace, logger);
                weeksByElement[periodicDayType] = weeks;
            }

            return weeks;
        }

        private static IReadOnlyList<(DateOnly StartDate, DateOnly EndDate)> ResolveDateRanges(XElement? dateRanges, XNamespace xmlNamespace, Dictionary<XElement, IReadOnlyList<(DateOnly StartDate, DateOnly EndDate)>> dateRangesByElement)
        {
            // can be null in here for special date operation since majority of bus lines does not have this
            if (dateRanges is null)
                return [];

            if (!dateRangesByElement.TryGetValue(dateRanges, out IReadOnlyList<(DateOnly StartDate, DateOnly EndDate)>? ranges))
            {
                ranges = ParseDateRanges(dateRanges, xmlNamespace);
                dateRangesByElement[dateRanges] = ranges;
            }

            return ranges;
        }

        private static IReadOnlyList<string> ResolvePublicHolidays(XElement? publicHolidays, XNamespace xmlNamespace, ILogger logger, Dictionary<XElement, IReadOnlyList<string>> publicHolidaysByElement)
        {
            if (publicHolidays is null)
                return [];

            // check does it have duplicate before parsing
            if (!publicHolidaysByElement.TryGetValue(publicHolidays, out IReadOnlyList<string>? holidays))
            {
                holidays = ParsePublicHolidays(publicHolidays, xmlNamespace, logger);
                publicHolidaysByElement[publicHolidays] = holidays;
            }

            return holidays;
        }


        // <WeekNumber> is an enumeration rather than a number, so the digits the schema guide describes are not
        // accepted: with a flags enum "3" would read as first-and-second rather than third.
        private static WeekOfMonth ParseWeeksOfMonth(XElement periodicDayType, XNamespace xmlNamespace, ILogger logger)
        {
            WeekOfMonth weeks = WeekOfMonth.None;

            foreach (XElement weekOfMonth in periodicDayType.Elements(xmlNamespace + "WeekOfMonth"))
            {
                foreach (XElement weekNumber in weekOfMonth.Elements(xmlNamespace + "WeekNumber"))
                {
                    switch (weekNumber.Value.Trim().ToLowerInvariant())
                    {
                        case "first": weeks |= WeekOfMonth.First; break;
                        case "second": weeks |= WeekOfMonth.Second; break;
                        case "third": weeks |= WeekOfMonth.Third; break;
                        case "fourth": weeks |= WeekOfMonth.Fourth; break;
                        case "fifth": weeks |= WeekOfMonth.Fifth; break;
                        case "last": weeks |= WeekOfMonth.Last; break;

                        // Skipped rather than rejected, so one odd value cannot cost the whole document. The
                        // journey then keeps its regular days unnarrowed, which is the safer way to be wrong.
                        default:
                            logger.LogWarning("Skipped <{WeekNumber}>, which is not a known WeekNumber value.", weekNumber.Value.Trim());
                            break;
                    }
                }
            }

            return weeks;
        }


        private static IReadOnlyList<(DateOnly StartDate, DateOnly EndDate)> ParseDateRanges(XElement dateRanges, XNamespace xmlNamespace)
        {
            var ranges = new List<(DateOnly StartDate, DateOnly EndDate)>();

            foreach (XElement dateRange in dateRanges.Elements(xmlNamespace + "DateRange"))
            {
                DateOnly? startDate = dateRange.Value(xmlNamespace, "StartDate").ParseDateOnly();
                if (startDate is null)
                    continue;

                // Both ends are inclusive, and a range left open at the end covers its start day only.
                DateOnly endDate = dateRange.Value(xmlNamespace, "EndDate").ParseDateOnly() ?? startDate.Value;
                ranges.Add((startDate.Value, endDate));
            }

            return ranges;
        }


        private static HashSet<DayOfWeek> ParseDaysOfWeek(XElement regularDayType, XNamespace xmlNamespace)
        {
            var days = new HashSet<DayOfWeek>();

            // <HolidaysOnly/> means the journey never runs on a regular weekday.
            if (regularDayType.Element(xmlNamespace + "HolidaysOnly") is not null)
                return days;

            XElement? daysOfWeek = regularDayType.Element(xmlNamespace + "DaysOfWeek");
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


        private static IReadOnlyList<string> ParsePublicHolidays(XElement holidays, XNamespace xmlNamespace, ILogger logger)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (XElement day in holidays.Elements())
            {
                switch (day.Name.LocalName)
                {
                    // Umbrella tags standing in for a whole group of days, expanded so a journey is stored against the
                    // individual holidays either way it was written.
                    case "AllBankHolidays":
                        names.UnionWith(s_christmasDays);
                        names.UnionWith(s_otherBankHolidayDays);
                        names.UnionWith(s_holidayMondays);
                        names.UnionWith(s_displacementHolidays);
                        break;

                    case "Christmas":
                        names.UnionWith(s_christmasDays);
                        break;

                    case "AllHolidaysExceptChristmas":
                        names.UnionWith(s_otherBankHolidayDays);
                        names.UnionWith(s_holidayMondays);
                        break;

                    case "HolidayMondays":
                        names.UnionWith(s_holidayMondays);
                        break;

                    case "DisplacementHolidays":
                        names.UnionWith(s_displacementHolidays);
                        break;

                    case "EarlyRunOffDays":
                        names.UnionWith(s_earlyRunOffDays);
                        break;

                    // A one-off holiday such as CoronationOfKingCharlesIII, named only by its Description.
                    case "OtherPublicHoliday":
                        string? description = day.Value(xmlNamespace, "Description");
                        if (description is null)
                        {
                            logger.LogWarning("Skipped <OtherPublicHoliday> with no <Description>.");
                        }
                        else
                        {
                            /*
                             <OtherPublicHoliday> 
                                <Date>2023-05-08</Date>
                                <Description>Coronation of King Charles III</Description>
                             </OtherPublicHoliday>
                             */
                            names.Add(description);
                        }

                        break;

                    // Every remaining tag names a single holiday and is stored under that name as it stands.
                    default:
                        names.Add(day.Name.LocalName);
                        break;
                }
            }

            var normalizedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                var normalizedName = new StringBuilder(name.Length);
                foreach (char character in name)
                {
                    /*
                     "New Year's Day"    → NEWYEARSDAY
                     "NewYearsDay"       → NEWYEARSDAY
                     "St. Andrew's Day"  → STANDREWSDAY
                     */
                    if (char.IsLetterOrDigit(character))
                    {
                        normalizedName.Append(char.ToUpperInvariant(character));
                    }
                }

                if (normalizedName.Length > 0)
                {
                    normalizedNames.Add(normalizedName.ToString());
                }
            }

            return [.. normalizedNames];
        }


        private static IEnumerable<DayOfWeek> AllDaysExcept(params DayOfWeek[] excluded)
        {
            return Enum.GetValues<DayOfWeek>().Where(d => !excluded.Contains(d));
        }
    }
}
