using Backend.Enumerations;
using Backend.Extensions;
using Backend.Models;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Backend.Services
{
    public class BusTimetableImportService : BackgroundService
    {
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

        public BusTimetableImportService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
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

        private async Task<bool> DownloadDataset(int datasetId, CancellationToken cancellationToken)
        {
            string url = string.Format(DownloadUrlFormat, datasetId);

            try
            {
                HttpClient client = _httpClientFactory.CreateClient();

                using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                using MemoryStream zipStream = new MemoryStream();
                using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    await source.CopyToAsync(zipStream, cancellationToken);
                }

                zipStream.Position = 0; // rewind before ZipArchive reads it
                using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using Stream xmlStream = await entry.OpenAsync(cancellationToken);
                        IReadOnlyList<BusTimetable> timetables = ParseTimetables(xmlStream);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            return false;
        }
        

        public IReadOnlyList<BusTimetable> ParseTimetables(Stream xmlStream)
        {
            XDocument doc = XDocument.Load(xmlStream);
            XElement root = doc.Root ?? throw new InvalidDataException("Empty TransXChange document.");


            // operator id -> national operator code
            Dictionary<string, string> operators =
                root.Element(Txc + "Operators")?
                    .Elements(Txc + "Operator")
                    .ToDictionary(
                        o => o.Attribute("id")?.Value ?? throw new InvalidDataException("Operator id not found."),
                        o => o.Value(Txc, "NationalOperatorCode") ?? o.Value(Txc, "OperatorCode") ?? throw new InvalidDataException("Operator element not found."))
                ?? throw new InvalidDataException("Operator element not found.");


            // journey pattern section id -> ordered timing links
            Dictionary<string, List<XElement>> sections =
                root.Element(Txc + "JourneyPatternSections")?
                    .Elements(Txc + "JourneyPatternSection")
                    .ToDictionary(
                        s => s.Attribute("id")?.Value ?? throw new InvalidDataException("Journey pattern id not found."),
                        s => s.Elements(Txc + "JourneyPatternTimingLink").ToList())
                ?? throw new InvalidDataException("Journey pattern section not found.");


            // Pre-read all vehicle journeys once.
            List<XElement> vehicleJourneys = root.Element(Txc + "VehicleJourneys")?.Elements(Txc + "VehicleJourney").ToList() 
                ?? throw new InvalidDataException("Vehicle journeys not found.");


            List<BusTimetable> busTimetables = [];
            IEnumerable<XElement> busServices = root.Element(Txc + "Services")?.Elements(Txc + "Service") ?? throw new InvalidDataException("Bus services not found.");
            foreach (XElement service in busServices)
            {
                string serviceCode = service.Value(Txc, "ServiceCode") ?? throw new InvalidDataException("Service code not found.");

                string lineName = service.Element(Txc + "Lines")?.Element(Txc + "Line")?.Value(Txc, "LineName") 
                    ?? throw new InvalidDataException("Line number not found.");

                string operatorRef = service.Value(Txc, "RegisteredOperatorRef") ?? throw new InvalidDataException("Operator ref not found.");
                string operatorCode = operators.GetValueOrDefault(operatorRef, "unknown");

                XElement? period = service.Element(Txc + "OperatingPeriod");
                DateOnly validFrom = period.Value(Txc, "StartDate").ParseDateOnly() ?? throw new InvalidDataException("Valid from not found.");
                DateOnly validTo = period.Value(Txc, "EndDate").ParseDateOnly() ?? DateOnly.FromDateTime(DateTime.Now.AddDays(30)); // sometime value cannot be found

                XElement? serviceProfile = service.Element(Txc + "OperatingProfile");

                XElement? standard = service.Element(Txc + "StandardService");
                string origin = standard?.Value(Txc, "Origin") ?? throw new InvalidDataException("Origin not found.");
                string destination = standard?.Value(Txc, "Destination") ?? throw new InvalidDataException("Destination not found.");

                // journey pattern id -> (direction, ordered section refs)
                Dictionary<string, (string? direction, List<string> sectionRefs)> patterns =
                    standard?.Elements(Txc + "JourneyPattern")
                        .ToDictionary(
                            jp => jp.Attribute("id")?.Value ?? throw new InvalidDataException("Journey pattern id not found."),
                            jp => (
                                jp.Value(Txc, "Direction"),
                                jp.Elements(Txc + "JourneyPatternSectionRefs").Select(r => r.Value).ToList()))
                    ?? throw new InvalidDataException("Patterns element not found.");

                foreach (XElement vehicleJourney in vehicleJourneys)
                {
                    // keep only journeys belonging to this service
                    string vjServiceRef = vehicleJourney.Value(Txc, "ServiceRef") ?? "";
                    if (!string.IsNullOrEmpty(vjServiceRef)
                        && !string.IsNullOrEmpty(serviceCode)
                        && vjServiceRef != serviceCode)
                    {
                        continue;
                    }

                    string jpRef = vehicleJourney.Element(Txc + "JourneyPatternRef")?.Value ?? "";
                    if (!patterns.TryGetValue(jpRef, out var pattern))
                        continue; // TODO: resolve VehicleJourneyRef inheritance

                    TimeOnly? firstDepature = vehicleJourney.Value(Txc, "DepartureTime").ParseTimeOnly();
                    if (firstDepature is null)
                        continue;

                    // vehicle-journey profile overrides the service default
                    XElement? profile = vehicleJourney.Element(Txc + "OperatingProfile") ?? serviceProfile;
                    (bool mon, bool tue, bool wed, bool thu, bool fri, bool sat, bool sun) = ParseDays(profile);
                    bool runsBankHols = ParseBankHolidays(profile);

                    List<XElement> links = pattern.sectionRefs
                        .Where(sections.ContainsKey)
                        .SelectMany(r => sections[r])
                        .ToList();

                    if (links.Count == 0)
                        continue;

                    List<BusCallingPoint> stops = BuildStops(links, firstDepature.Value);

                    string vehicleJourneyId = vehicleJourney.Attribute("id")?.Value
                        ?? vehicleJourney.Value(Txc, "PrivateCode")
                        ?? Guid.NewGuid().ToString();

                    busTimetables.Add(new BusTimetable
                    {
                        Id = $"{operatorCode}-{vehicleJourneyId}",
                        OperatorRef = operatorCode,
                        LineName = lineName,
                        OriginName = origin,
                        DestinationName = destination,
                        Direction = string.Equals(pattern.direction, "inbound", StringComparison.OrdinalIgnoreCase)
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

            return busTimetables;
        }


        // Walk the ordered timing links, accumulating run/wait times from the departure.
        private static List<BusCallingPoint> BuildStops(List<XElement> links, TimeOnly firstDeparture)
        {
            List<BusCallingPoint> stops = new();
            TimeOnly current = firstDeparture;
            int sequence = 1;

            for (int i = 0; i < links.Count; i++)
            {
                XElement link = links[i];
                XElement? from = link.Element(Txc + "From");
                XElement? to = link.Element(Txc + "To");

                // First link's "From" is the origin — departs at the journey departure time.
                if (i == 0)
                {
                    current = AddWait(current, from);
                    stops.Add(MakeStop(sequence++, from, arrival: null, departure: current));
                }

                // Travel to the "To" stop.
                TimeSpan runTime = ParseDuration(link.Value(Txc, "RunTime"));
                TimeOnly arrival = current.Add(runTime);
                TimeOnly afterWait = AddWait(arrival, to);
                current = afterWait;

                bool isLast = i == links.Count - 1;
                stops.Add(MakeStop(
                    sequence++, to,
                    arrival: arrival,
                    departure: isLast ? null : afterWait));
            }

            return stops;
        }

        private static BusCallingPoint MakeStop(int sequence, XElement? usage, TimeOnly? arrival, TimeOnly? departure)
        {
            string stopRef = usage.Value(Txc, "StopPointRef") ?? throw new InvalidDataException("Bus stop ref not found.");

            return new BusCallingPoint
            {
                Id = sequence,
                BusTimetableId = "13",
                Sequence = sequence,
                BusStopId = stopRef,
                ArrivalTime = arrival,
                DepartureTime = departure,
            };
        }


        private static (bool, bool, bool, bool, bool, bool, bool) ParseDays(XElement? profile)
        {
            XElement? days = profile?
                .Element(Txc + "RegularDayType")?
                .Element(Txc + "DaysOfWeek");

            bool mon = false, tue = false, wed = false, thu = false, fri = false, sat = false, sun = false;
            if (days is null)
                return (mon, tue, wed, thu, fri, sat, sun);

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
                }
            }

            return (mon, tue, wed, thu, fri, sat, sun);
        }

        // Simplified: true if any bank-holiday DaysOfOperation are listed.
        // TODO: for accuracy, read BankHolidayOperation/DaysOfOperation vs DaysOfNonOperation
        // and SpecialDaysOperation date lists into explicit exception dates.
        private static bool ParseBankHolidays(XElement? profile)
        {
            XElement? operation = profile?
                .Element(Txc + "BankHolidayOperation")?
                .Element(Txc + "DaysOfOperation");

            return operation is not null && operation.Elements().Any();
        }

        // ISO 8601 duration, e.g. "PT4M30S" -> 4m30s.
        private static TimeSpan ParseDuration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TimeSpan.Zero;
            try { return XmlConvert.ToTimeSpan(value); }
            catch { return TimeSpan.Zero; }
        }

        private static TimeOnly AddWait(TimeOnly time, XElement? usage)
        {
            string? wait = usage?.Value(Txc, "WaitTime");
            return string.IsNullOrWhiteSpace(wait) ? time : time.Add(ParseDuration(wait));
        }
    }
}