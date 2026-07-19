using Backend.Models;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Backend.Services
{
    public class BusLocationTrackingService : BackgroundService
    {
        // refresh every 10 seconds
        private const string BulkArchiveUrl = "https://data.bus-data.dft.gov.uk/avl/download/bulk_archive";
        private const string UnknownValue = "unknown";

        private static readonly XNamespace SiriNamespace = "http://www.siri.org.uk/siri";
        private static readonly TimeZoneInfo _ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TransportDataStore _transportDataStore;
        private readonly ILogger _logger;

        public BusLocationTrackingService(IHttpClientFactory httpClientFactory, TransportDataStore transportDataStore, ILogger<BusLocationTrackingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _transportDataStore = transportDataStore;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, BulkArchiveUrl);

                    HttpClient client = _httpClientFactory.CreateClient();
                    using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

                    response.EnsureSuccessStatusCode();

                    using MemoryStream archiveStream = new MemoryStream();
                    using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await responseStream.CopyToAsync(archiveStream, cancellationToken);

                    archiveStream.Position = 0;

                    using ZipArchive zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

                    IReadOnlyList<BusLocation> busLocations = await ImportBusLocations(zipArchive, cancellationToken);
                    _transportDataStore.RefreshBusLocations(busLocations);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download and extract bus locations from zip archive");
                }
                finally
                {
                    await Task.Delay(10000, cancellationToken);
                }
            }
        }

        private async Task<IReadOnlyList<BusLocation>> ImportBusLocations(ZipArchive zipArchive, CancellationToken cancellationToken)
        {
            List<BusLocation> busLocations = [];

            foreach (ZipArchiveEntry entry in zipArchive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using Stream entryStream = await entry.OpenAsync(cancellationToken);
                XDocument document = await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken);


                IEnumerable<XElement> activities = document.Descendants(SiriNamespace + "VehicleActivity");
                foreach (XElement activity in activities)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        XElement? journey = activity.Element(SiriNamespace + "MonitoredVehicleJourney");
                        XElement? location = journey?.Element(SiriNamespace + "VehicleLocation");

                        string? itemIdentifier = Value(activity, "ItemIdentifier");
                        DateTime? recordedAtTime = ParseUkDateTime(Value(activity, "RecordedAtTime"));
                        if (recordedAtTime is null || DateTime.Now - TimeSpan.FromMinutes(5) > recordedAtTime.Value)
                            continue;

                        string? operatorRef = Value(journey, "OperatorRef");
                        string? publishedLineName = Value(journey, "PublishedLineName");

                        string originName = Value(journey, "OriginName") ?? UnknownValue;
                        string originRef = Value(journey, "OriginRef") ?? UnknownValue;
                        string destinationName = Value(journey, "DestinationName") ?? UnknownValue;
                        string destinationRef = Value(journey, "DestinationRef") ?? UnknownValue;

                        TimeOnly? originAimedDepartureTime = ParseUkTimeOnly(Value(journey, "OriginAimedDepartureTime"));
                        TimeOnly? destinationAimedArrivalTime = ParseUkTimeOnly(Value(journey, "DestinationAimedArrivalTime"));

                        TimeOnly? departureTimeFromJourneyRef = ParseTimeOnly(Value(journey?.Element(SiriNamespace + "FramedVehicleJourneyRef"), "DatedVehicleJourneyRef"));

                        string? rawJourneyCode = ExtensionValue(activity, "JourneyCode");
                        TimeOnly? departureTimeFromJourneyCode = rawJourneyCode == "0000" ? null : ParseTimeOnly(rawJourneyCode); // journey code. "0000" is that machine's null sentinel 

                        string? vehicleRef = Value(journey, "VehicleRef");

                        decimal? latitude = ParseDecimal(Value(location, "Latitude"));
                        decimal? longitude = ParseDecimal(Value(location, "Longitude"));
                        decimal bearing = ParseDecimal(Value(journey, "Bearing")) ?? 0;

                        if (itemIdentifier is null || recordedAtTime is null || operatorRef is null || publishedLineName is null || vehicleRef is null || latitude is null || longitude is null)
                            continue;

                        busLocations.Add(new BusLocation
                        {
                            Id = $"{operatorRef}-{vehicleRef}",
                            RecordedAtTime = recordedAtTime.Value,

                            OperatorRef = operatorRef,
                            PublishedLineName = publishedLineName,

                            OriginName = originName,
                            OriginRef = originRef,
                            OriginAimedDepartureTime = originAimedDepartureTime ?? departureTimeFromJourneyCode ?? departureTimeFromJourneyRef,

                            DestinationName = destinationName,
                            DestinationRef = destinationRef,
                            DestinationAimedArrivalTime = destinationAimedArrivalTime,

                            VehicleRef = vehicleRef,

                            Latitude = latitude.Value,
                            Longitude = longitude.Value,
                            Bearing = bearing
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to import bus locations from siri.xml");
                    }
                }
            }

            return busLocations;
        }

        private static string? ExtensionValue(XElement activity, string localName)
        {
            XElement? extensions =
                activity.Element(SiriNamespace + "Extensions") ??
                activity.Element("Extensions");

            string? value = extensions?
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName == localName)?
                .Value
                .Trim();

            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string? Value(XElement? parent, string elementName)
        {
            string? value = parent?.Element(SiriNamespace + elementName)?.Value;
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        private static DateTime? ParseUkDateTime(string? value)
        {
            return DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset result)
                    ? TimeZoneInfo.ConvertTime(result, _ukTimeZone).DateTime
                    : null;
        }

        private static TimeOnly? ParseUkTimeOnly(string? value)
        {
            return DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset result)
                    ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(result, _ukTimeZone).DateTime)
                    : null;
        }

        private static TimeOnly? ParseTimeOnly(string? value)
        {
            return TimeOnly.TryParseExact(
                value, ["HHmm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly result)
                    ? result
                    : null;
        }

        private static decimal? ParseDecimal(string? value)
        {
            return decimal.TryParse(value, out decimal result)
                ? result
                : null;
        }
    }
}