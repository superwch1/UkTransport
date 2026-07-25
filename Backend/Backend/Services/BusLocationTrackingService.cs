using Backend.Models;
using Backend.Extensions;
using System.IO.Compression;
using System.Xml.Linq;
using System.Collections.Frozen;

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

                    FrozenDictionary<string, BusLocation> busLocations = await ImportBusLocations(zipArchive, cancellationToken);
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

        private async Task<FrozenDictionary<string, BusLocation>> ImportBusLocations(ZipArchive zipArchive, CancellationToken cancellationToken)
        {
            Dictionary<string, BusLocation> busLocations = [];

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

                        DateTime? recordedAtTime = activity.Value(SiriNamespace, "RecordedAtTime").ParseUkDateTime(_ukTimeZone);
                        if (recordedAtTime is null || DateTime.Now - TimeSpan.FromMinutes(10) > recordedAtTime.Value)
                            continue;

                        string? operatorRef = journey.Value(SiriNamespace, "OperatorRef");
                        string? publishedLineName = journey.Value(SiriNamespace, "PublishedLineName");

                        string? originRef = journey.Value(SiriNamespace, "OriginRef");
                        string? destinationRef = journey.Value(SiriNamespace, "DestinationRef");
                        if (operatorRef is null || publishedLineName is null || originRef is null || destinationRef is null)
                            continue;

                        string originName = journey.Value(SiriNamespace, "OriginName") ?? UnknownValue;
                        string destinationName = journey.Value(SiriNamespace, "DestinationName") ?? UnknownValue;                        

                        TimeOnly? originAimedDepartureTime = journey.Value(SiriNamespace, "OriginAimedDepartureTime").ParseUkTimeOnly(_ukTimeZone);
                        TimeOnly? destinationAimedArrivalTime = journey.Value(SiriNamespace, "DestinationAimedArrivalTime").ParseUkTimeOnly(_ukTimeZone);

                        string? rawJourneyCode = activity.ExtensionValue(SiriNamespace, "JourneyCode");
                        TimeOnly? departureTimeFromJourneyCode = rawJourneyCode == "0000" ? null : rawJourneyCode.ParseTimeOnly(format: ["HHmm", "HH:mm:ss"]); // "0000" is that machine's null sentinel

                        TimeOnly? departureTimeFromJourneyRef = journey?
                            .Element(SiriNamespace + "FramedVehicleJourneyRef")
                            .Value(SiriNamespace, "DatedVehicleJourneyRef")
                            .ParseTimeOnly(format: ["HHmm", "HH:mm:ss"]);

                        // seems most of the car either do not have do not have arrival time or both arrival and departure time
                        originAimedDepartureTime = originAimedDepartureTime ?? departureTimeFromJourneyCode ?? departureTimeFromJourneyRef;
                        if (!originAimedDepartureTime.HasValue)
                            continue;

                        decimal? latitude = location.Value(SiriNamespace, "Latitude").ParseDecimal();
                        decimal? longitude = location.Value(SiriNamespace, "Longitude").ParseDecimal();
                        decimal bearing = journey.Value(SiriNamespace, "Bearing").ParseDecimal() ?? 0;

                        if (latitude is null || longitude is null)
                            continue;

                        string originDepartureKey = BusTimeTableExtension.CreateOriginDepartureKey(originAimedDepartureTime.Value, originRef, destinationRef);
                        busLocations[originDepartureKey] = new BusLocation
                        {
                            OriginDepartureKey = originDepartureKey,
                            RecordedAtTime = recordedAtTime.Value,

                            OperatorRef = operatorRef,
                            PublishedLineName = publishedLineName,

                            OriginName = originName,
                            OriginRef = originRef,
                            OriginAimedDepartureTime = originAimedDepartureTime,

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
                        _logger.LogError(ex, "Failed to import bus locations from siri.xml");
                    }
                }
            }

            return busLocations.ToFrozenDictionary();
        }
    }
}