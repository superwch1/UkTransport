using Backend.Enumerations;
using Backend.Extensions;
using Backend.Models;
using Backend.Repositories;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Backend.Services
{
    public class BusTimetableImportService : BackgroundService
    {
        private static readonly string LogPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
    "log.txt");

        private const string DatasetApiUrl = "https://data.bus-data.dft.gov.uk/api/v1/dataset/";
        private const string DownloadUrlFormat = "https://data.bus-data.dft.gov.uk/timetable/dataset/{0}/download/";
        private static readonly XNamespace Txc = "http://www.transxchange.org.uk/";

        private const int PageSize = 1000;
        private readonly string _apiKey;

        // pause between downloads so we don't flood the server.
        private static readonly TimeSpan DelayBetweenDownloads = TimeSpan.FromSeconds(1);

        // Timetables change slowly; re-run once a day.
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;

        public BusTimetableImportService(IHttpClientFactory httpClientFactory, IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
            _apiKey = configuration["ApiKey"] ?? throw new InvalidOperationException("ApiKey is not configured.");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<int> datasetIds = await GetDatasetIds(cancellationToken);
                foreach (int datasetId in datasetIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await DownloadDataset(datasetId, cancellationToken);
                    await Task.Delay(DelayBetweenDownloads, cancellationToken);
                }

                await Task.Delay(RefreshInterval, cancellationToken);
            }
        }

        private async Task<IReadOnlyList<int>> GetDatasetIds(CancellationToken cancellationToken)
        {
            try
            {
                HttpClient client = _httpClientFactory.CreateClient();

                List<int> ids = [];
                int offset = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string url = $"{DatasetApiUrl}?api_key={_apiKey}&status=published&limit={PageSize}&offset={offset}";

                    using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                    if (!document.RootElement.TryGetProperty("results", out JsonElement results) || results.GetArrayLength() == 0)
                        break;

                    foreach (JsonElement dataset in results.EnumerateArray())
                    {
                        if (dataset.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt32(out int id))
                        {
                            ids.Add(id);
                        }
                    }

                    offset += PageSize;
                }

                return ids;
            }
            catch
            {
                return [];
            }
        }

        private async Task DownloadDataset(int datasetId, CancellationToken cancellationToken)
        {
            string url = string.Format(DownloadUrlFormat, datasetId);

            try
            {
                HttpClient client = _httpClientFactory.CreateClient();

                LogMessage(url);

                using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                using MemoryStream buffered = new MemoryStream();
                using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    await source.CopyToAsync(buffered, cancellationToken);
                }
                buffered.Position = 0;

                // BODS returns EITHER a zip of xml files OR a single xml file.
                Span<byte> header = stackalloc byte[4];
                int read = buffered.Read(header);
                buffered.Position = 0;

                bool isZip = read == 4
                    && header[0] == 0x50 && header[1] == 0x4B   // 'P' 'K'
                    && header[2] == 0x03 && header[3] == 0x04;

                if (!isZip)
                {
                    // Single XML dataset — parse the buffer directly.
                    try
                    {
                        await ImportTimetables(buffered);
                    }
                    catch (Exception ex)
                    {
                        LogException(ex.Message);
                        Console.WriteLine(ex);
                    }
                }
                else
                {
                    using ZipArchive archive = new ZipArchive(buffered, ZipArchiveMode.Read);
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Skip macOS metadata and non-xml entries.
                        // https://data.bus-data.dft.gov.uk/timetable/dataset/24170/download/
                        if (entry.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            using Stream xmlStream = await entry.OpenAsync(cancellationToken);
                            await ImportTimetables(xmlStream);
                        }
                        catch (Exception ex)
                        {
                            LogException(ex.Message);
                            Console.WriteLine(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                LogException(ex.Message);
            }
        }


        public async Task ImportTimetables(Stream xmlStream)
        {
            // Injectable "today" so the EndDate fallback is deterministic and testable.
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

            XDocument doc = XDocument.Load(xmlStream);
            XElement root = doc.Root ?? throw new InvalidDataException("Empty TransXChange document.");

            // <Operators> may contain <Operator> OR <LicensedOperator>; read both,
            Dictionary<string, string> operators =
                (root.Element(Txc + "Operators") ?? throw new InvalidDataException("<Operators> element not found."))
                .Elements()
                .Where(o => o.Name == Txc + "Operator" || o.Name == Txc + "LicensedOperator")
                .ToDictionary(
                    o => o.Attribute("id")?.Value ?? throw new InvalidDataException("Operator 'id' attribute not found."),
                    o => o.Value(Txc, "NationalOperatorCode") ?? o.Value(Txc, "OperatorCode")
                        ?? throw new InvalidDataException("Operator code (NationalOperatorCode/OperatorCode) not found."));


            // journey pattern section id -> ordered timing links
            Dictionary<string, List<XElement>> sections =
                (root.Element(Txc + "JourneyPatternSections")
                    ?? throw new InvalidDataException("<JourneyPatternSections> element not found."))
                .Elements(Txc + "JourneyPatternSection")
                .ToDictionary(
                    s => s.Attribute("id")?.Value ?? throw new InvalidDataException("JourneyPatternSection 'id' attribute not found."),
                    s => s.Elements(Txc + "JourneyPatternTimingLink").ToList());


            // Pre-read all vehicle journeys once.
            List<XElement> vehicleJourneys =
                (root.Element(Txc + "VehicleJourneys") ?? throw new InvalidDataException("<VehicleJourneys> element not found."))
                .Elements(Txc + "VehicleJourney")
                .ToList();


            // index journeys by VehicleJourneyCode so <VehicleJourneyRef> inheritance can be resolved
            Dictionary<string, XElement> journeysByCode = vehicleJourneys
                .Select(vj => (Code: vj.Value(Txc, "VehicleJourneyCode"), Journey: vj))
                .Where(t => t.Code is not null)
                .GroupBy(t => t.Code!)
                .ToDictionary(g => g.Key, g => g.First().Journey);


            // O(services + journeys) instead of scanning every journey per service. Journeys with no ServiceRef land in the "" bucket and are
            // offered to every service (pattern lookup then scopes them correctly since JourneyPattern ids are document-unique).
            ILookup<string, XElement> journeysByService = vehicleJourneys.ToLookup(vj => vj.Value(Txc, "ServiceRef") ?? throw new InvalidDataException("Service Ref not found."));

            List<BusTimetable> busTimetables = [];

            IEnumerable<XElement> busServices = root.Element(Txc + "Services")?.Elements(Txc + "Service") ?? throw new InvalidDataException("<Services> element not found.");
            foreach (XElement service in busServices)
            {
                string serviceCode = service.Value(Txc, "ServiceCode") ?? throw new InvalidDataException("ServiceCode not found.");

                // a service may carry multiple <Line> elements; take the first that actually has a LineName rather than only Lines[0].
                string lineName = service.Element(Txc + "Lines")?
                        .Elements(Txc + "Line")
                        .Select(l => l.Value(Txc, "LineName"))
                        .FirstOrDefault(n => n is not null)
                    ?? throw new InvalidDataException("LineName not found.");

                string operatorRef = service.Value(Txc, "RegisteredOperatorRef") ?? throw new InvalidDataException("RegisteredOperatorRef not found.");

                // fix the problem with <RegisteredOperatorRef>regHRSC</RegisteredOperatorRef> not matching <OperatorCode>HRSC</OperatorCode>  
                // so choose the first one if the dataset only consist one code
                // https://data.bus-data.dft.gov.uk/timetable/dataset/13088/download/

                string operatorCode;
                if (operators.Count == 1)
                {
                    operatorCode = operators.Values.First();
                }
                else if (operators.TryGetValue(operatorRef, out string? code) && code is not null)
                {
                    operatorCode = code;
                }
                else
                {
                    throw new InvalidDataException("Operator code not found.");
                }
                    

                XElement? period = service.Element(Txc + "OperatingPeriod");
                DateOnly validFrom = period.Value(Txc, "StartDate").ParseDateOnly() ?? throw new InvalidDataException("OperatingPeriod/StartDate not found.");

                // EndDate is legitimately absent in some files; deterministic fallback.
                DateOnly validTo = period.Value(Txc, "EndDate").ParseDateOnly() ?? today.AddDays(180);

                XElement? serviceProfile = service.Element(Txc + "OperatingProfile");

                XElement standard = service.Element(Txc + "StandardService") ?? throw new InvalidDataException("<StandardService> element not found.");
                string origin = standard.Value(Txc, "Origin") ?? throw new InvalidDataException("StandardService/Origin not found.");
                string destination = standard.Value(Txc, "Destination") ?? throw new InvalidDataException("StandardService/Destination not found.");

                // journey pattern id -> (direction, ordered section refs)
                // ("JourneyPatternSectionRefs" — plural, one ref per element — is the correct TXC element name.)
                Dictionary<string, (string? Direction, List<string> SectionRefs)> patterns =
                    standard.Elements(Txc + "JourneyPattern")
                        .ToDictionary(
                            jp => jp.Attribute("id")?.Value ?? throw new InvalidDataException("JourneyPattern 'id' attribute not found."),
                            jp => (
                                jp.Value(Txc, "Direction"),
                                jp.Elements(Txc + "JourneyPatternSectionRefs")
                                  .Select(r => r.Value.Trim())
                                  .ToList()));

                // Candidate journeys: those referencing this service, plus any with no ServiceRef at all (rare, but tolerated).
                IEnumerable<XElement> candidates = journeysByService[serviceCode].Concat(journeysByService[""]);
                foreach (XElement vehicleJourney in candidates)
                {
                    // resolve JourneyPatternRef through the VehicleJourneyRef inheritance chain instead of only looking at the journey itself.
                    string? jpRef = ResolveInherited(vehicleJourney, journeysByCode, "JourneyPatternRef")?.Value.Trim();
                    if (jpRef is null || !patterns.TryGetValue(jpRef, out var pattern))
                        continue;

                    // DepartureTime is mandatory on each VehicleJourney (not inherited).
                    (TimeOnly Time, int DayOffset)? departure = ParseJourneyTime(vehicleJourney.Value(Txc, "DepartureTime"));
                    if (departure is null)
                        continue;

                    // journey-level profile (own or inherited) replaces the service default wholesale — no merging, per TXC semantics.
                    XElement? profile = ResolveInherited(vehicleJourney, journeysByCode, "OperatingProfile") ?? serviceProfile;

                    (bool mon, bool tue, bool wed, bool thu, bool fri, bool sat, bool sun) = ParseDays(profile);
                    bool runsBankHols = ParseBankHolidays(profile);

                    // FIX: previously missing section refs were silently skipped,
                    // which stitches non-adjacent sections into one wrong chain of
                    // stops/times. Skip the whole journey instead.
                    if (pattern.SectionRefs.Any(r => !sections.ContainsKey(r)))
                        continue;

                    List<XElement> links = pattern.SectionRefs
                        .SelectMany(r => sections[r])
                        .ToList();

                    if (links.Count == 0)
                        continue;

                    // FIX: VehicleJourney has no 'id' attribute in TXC; its identity
                    // is <VehicleJourneyCode>. Fallback chain is now deterministic —
                    // no Guid.NewGuid(), so ids are stable across runs (safe upserts).
                    string vehicleJourneyId =
                        vehicleJourney.Value(Txc, "VehicleJourneyCode")
                        ?? vehicleJourney.Value(Txc, "PrivateCode")
                        ?? vehicleJourney.Attribute("id")?.Value
                        ?? $"{serviceCode}-{jpRef}-{departure.Value.Time:HHmmss}+{departure.Value.DayOffset}";

                    string timetableId = Guid.NewGuid().ToString(); // $"{operatorCode}-{vehicleJourneyId}";
                    Dictionary<string, XElement> overrides = CollectTimingOverrides(vehicleJourney, journeysByCode);
                    List<BusCallingPoint> stops = BuildStops(links, overrides, departure.Value.Time, departure.Value.DayOffset, timetableId);

                    busTimetables.Add(new BusTimetable
                    {
                        Id = timetableId,
                        OperatorRef = operatorCode,
                        LineName = lineName,
                        OriginName = origin,
                        DestinationName = destination,
                        Direction = string.Equals(pattern.Direction, "inbound", StringComparison.OrdinalIgnoreCase)
                            ? Direction.Inbound
                            : Direction.Outbound,
                        ValidFrom = validFrom,
                        ValidTo = validTo,
                        Monday = mon,
                        Tuesday = tue,
                        Wednesday = wed,
                        Thursday = thu,
                        Friday = fri,
                        Saturday = sat,
                        Sunday = sun,
                        RunsOnBankHolidays = runsBankHols,
                        BusCallingPoints = stops
                    });
                }
            }

            using var scope = _scopeFactory.CreateScope();
            BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();
            await busRepository.CreateBusTimetables(busTimetables);
        }


        private static Dictionary<string, XElement> CollectTimingOverrides(XElement vehicleJourney, Dictionary<string, XElement> journeysByCode)
        {
            XElement? current = vehicleJourney;
            HashSet<string> visited = [];

            while (current is not null)
            {
                List<XElement> list = current.Elements(Txc + "VehicleJourneyTimingLink").ToList();
                if (list.Count > 0)
                {
                    Dictionary<string, XElement> map = [];
                    foreach (XElement l in list)
                    {
                        string? linkRef = l.Value(Txc, "JourneyPatternTimingLinkRef");
                        if (linkRef is not null)
                            map[linkRef] = l;
                    }
                    return map;
                }

                string? parentRef = current.Value(Txc, "VehicleJourneyRef");
                if (parentRef is null || !visited.Add(parentRef))
                    break;
                journeysByCode.TryGetValue(parentRef, out current);
            }

            return [];
        }


        private static XElement? ResolveInherited(XElement vehicleJourney, Dictionary<string, XElement> journeysByCode, string localName)
        {
            XElement? current = vehicleJourney;
            HashSet<string> visited = [];

            while (current is not null)
            {
                XElement? found = current.Element(Txc + localName);
                if (found is not null)
                    return found;

                string? parentRef = current.Value(Txc, "VehicleJourneyRef");
                if (parentRef is null || !visited.Add(parentRef))
                    return null; 

                journeysByCode.TryGetValue(parentRef, out current);
            }

            return null;
        }


        private static List<BusCallingPoint> BuildStops(List<XElement> links, Dictionary<string, XElement> overrides, TimeOnly firstDeparture, int startDayOffset, string timetableId)
        {
            List<BusCallingPoint> stops = new(links.Count + 1);
            int sequence = 1;
            int dayOffset = startDayOffset;

            XElement? firstFrom = links[0].Element(Txc + "From");
            stops.Add(MakeStop(sequence++, firstFrom, timetableId,
                arrival: null, arrivalDayOffset: null,
                departure: firstDeparture, departureDayOffset: dayOffset));

            TimeOnly current = firstDeparture;

            for (int i = 0; i < links.Count; i++)
            {
                XElement link = links[i];
                XElement? ovr = GetOverride(link, overrides);
                XElement? to = link.Element(Txc + "To");

                // Override RunTime wins; fall back to the pattern link's.
                TimeSpan runTime = ParseDuration(
                    ovr?.Value(Txc, "RunTime") ?? link.Value(Txc, "RunTime"));
                current = current.Add(runTime, out int runWraps);
                dayOffset += runWraps;

                TimeOnly arrival = current;
                int arrivalDayOffset = dayOffset;

                bool isLast = i == links.Count - 1;
                if (isLast)
                {
                    stops.Add(MakeStop(sequence++, to, timetableId,
                        arrival: arrival, arrivalDayOffset: arrivalDayOffset,
                        departure: null, departureDayOffset: null));
                    break;
                }

                XElement nextLink = links[i + 1];
                XElement? nextOvr = GetOverride(nextLink, overrides);

                current = AddWait(current, ovr?.Element(Txc + "To"), to, ref dayOffset);
                current = AddWait(current, nextOvr?.Element(Txc + "From"), nextLink.Element(Txc + "From"), ref dayOffset);

                stops.Add(MakeStop(sequence++, to, timetableId,
                    arrival: arrival, arrivalDayOffset: arrivalDayOffset,
                    departure: current, departureDayOffset: dayOffset));
            }

            return stops;
        }

        private static XElement? GetOverride(XElement link, Dictionary<string, XElement> overrides)
        {
            string? id = link.Attribute("id")?.Value;
            return id is not null && overrides.TryGetValue(id, out XElement? o) ? o : null;
        }

        // Wait time: override usage wins, then pattern usage.
        private static TimeOnly AddWait(TimeOnly time, XElement? overrideUsage, XElement? patternUsage, ref int dayOffset)
        {
            string? wait = overrideUsage?.Value(Txc, "WaitTime") ?? patternUsage?.Value(Txc, "WaitTime");
            if (string.IsNullOrWhiteSpace(wait))
                return time;

            TimeOnly result = time.Add(ParseDuration(wait), out int wraps);
            dayOffset += wraps;
            return result;
        }

        private static BusCallingPoint MakeStop(int sequence, XElement? usage, string timetableId,
            TimeOnly? arrival, int? arrivalDayOffset, TimeOnly? departure, int? departureDayOffset)
        {
            string stopRef = usage.Value(Txc, "StopPointRef")
                ?? throw new InvalidDataException("StopPointRef not found on timing-link stop usage.");

            return new BusCallingPoint
            {
                BusTimetableId = timetableId, 
                Sequence = sequence,
                BusStopId = stopRef,
                ArrivalTime = arrival,
                DepartureTime = departure,
                ArrivalDayOffset = arrivalDayOffset,
                DepartureDayOffset = departureDayOffset,
            };
        }

        private static (bool, bool, bool, bool, bool, bool, bool) ParseDays(XElement? profile)
        {
            XElement? regular = profile?.Element(Txc + "RegularDayType");

            // HolidaysOnly is a valid alternative to DaysOfWeek: the journey runs
            // only on holiday dates, never on regular weekdays.
            if (regular?.Element(Txc + "HolidaysOnly") is not null)
                return (false, false, false, false, false, false, false);

            XElement? days = regular?.Element(Txc + "DaysOfWeek");

            // FIX: previously "no profile" meant all-false, i.e. a journey that
            // exists but runs on no day at all. Per the TXC schema, the default
            // day pattern when none is specified is Monday to Friday. (The BODS
            // PTI profile requires an explicit OperatingProfile anyway, so this
            // is a defensive default.)
            if (days is null || !days.Elements().Any())
                return (true, true, true, true, true, false, false);

            bool mon = false, tue = false, wed = false, thu = false, fri = false, sat = false, sun = false;

            foreach (XElement el in days.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "Monday": mon = true; break;
                    case "Tuesday": tue = true; break;
                    case "Wednesday": wed = true; break;
                    case "Thursday": thu = true; break;
                    case "Friday": fri = true; break;
                    case "Saturday": sat = true; break;
                    case "Sunday": sun = true; break;
                    case "MondayToFriday": mon = tue = wed = thu = fri = true; break;
                    case "MondayToSaturday": mon = tue = wed = thu = fri = sat = true; break;
                    case "MondayToSunday": mon = tue = wed = thu = fri = sat = sun = true; break;
                    case "Weekend": sat = sun = true; break;

                    // FIX: valid TXC values that were previously ignored.
                    case "NotSaturday": mon = tue = wed = thu = fri = sun = true; break;
                    case "NotSunday": mon = tue = wed = thu = fri = sat = true; break;
                    case "NotMonday": tue = wed = thu = fri = sat = sun = true; break;
                    case "NotTuesday": mon = wed = thu = fri = sat = sun = true; break;
                    case "NotWednesday": mon = tue = thu = fri = sat = sun = true; break;
                    case "NotThursday": mon = tue = wed = fri = sat = sun = true; break;
                    case "NotFriday": mon = tue = wed = thu = sat = sun = true; break;
                }
            }

            return (mon, tue, wed, thu, fri, sat, sun);
        }

        // Still a simplification: a single bool cannot represent "runs on some
        // holidays but not others". Improvement over the previous version: an
        // explicit DaysOfNonOperation/AllBankHolidays now wins over a stray
        // DaysOfOperation entry.
        // TODO: expand BankHolidayOperation + SpecialDaysOperation into explicit
        // exception-date lists for full accuracy.
        private static bool ParseBankHolidays(XElement? profile)
        {
            XElement? bho = profile?.Element(Txc + "BankHolidayOperation");
            if (bho is null)
                return false;

            bool anyOperation =
                bho.Element(Txc + "DaysOfOperation")?.Elements().Any() == true;

            bool allNonOperation =
                bho.Element(Txc + "DaysOfNonOperation")?
                    .Elements()
                    .Any(e => e.Name.LocalName == "AllBankHolidays") == true;

            return anyOperation && !allNonOperation;
        }

        // ISO 8601 duration, e.g. "PT4M30S" -> 4m30s.
        private static TimeSpan ParseDuration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TimeSpan.Zero;
            try { return XmlConvert.ToTimeSpan(value); }
            catch { 

                return TimeSpan.Zero; 
            } // malformed durations degrade to zero; consider logging
        }

        private static TimeOnly AddWait(TimeOnly time, XElement? usage, ref int dayOffset)
        {
            string? wait = usage?.Value(Txc, "WaitTime");
            if (string.IsNullOrWhiteSpace(wait))
                return time;

            TimeOnly result = time.Add(ParseDuration(wait), out int wraps);
            dayOffset += wraps;
            return result;
        }

        // Parses a TransXChange departure/time string into a time-of-day plus a
        // day offset. TXC permits hours >= 24 to express times in a later operating
        // day (e.g. "25:30" == 01:30 on day 1). TimeOnly.TryParse rejects those, so
        // parse the fields directly. Returns null for genuinely malformed input.
        private static (TimeOnly Time, int DayOffset)? ParseJourneyTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string[] parts = value.Trim().Split(':');
            if (parts.Length is < 2 or > 3)
                return null;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes))
                return null;

            int seconds = 0;
            if (parts.Length == 3
                && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
                return null;

            if (hours < 0 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
                return null;

            int totalSeconds = (hours * 3600) + (minutes * 60) + seconds;
            const int secondsPerDay = 24 * 3600;

            int dayOffset = totalSeconds / secondsPerDay;
            TimeOnly time = new TimeOnly(0, 0).Add(TimeSpan.FromSeconds(totalSeconds % secondsPerDay));
            return (time, dayOffset);
        }

        private static void LogMessage(string message)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, entry);
        }

        private static void LogException(string exceptionMessage)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Environment.NewLine}{exceptionMessage}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(LogPath, entry);
        }
    }
}