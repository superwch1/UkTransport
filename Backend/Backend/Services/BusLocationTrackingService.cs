using Backend.Extensions;
using Backend.Models;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Xml.Linq;

namespace Backend.Services
{
    // https://data.bus-data.dft.gov.uk/avl/download/bulk_archive bulk data download in .xml format
    // https://data.bus-data.dft.gov.uk/avl/?status=live browse bus location data (before login .csv format, after login .xml format)
    // .xml - https://data.bus-data.dft.gov.uk/api/v1/datafeed/24550/?api_key=

    public class BusLocationTrackingService : BackgroundService
    {
        // refresh every 10 seconds
        private const string _unknownPlaceholder = "unknown";
        private static readonly XNamespace _siriNamespace = "http://www.siri.org.uk/siri";

        private readonly TimeService _timeService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TransportDataStore _transportDataStore;
        private readonly ILogger<BusLocationTrackingService> _logger;

        private readonly IReadOnlyDictionary<string, string> _apiKeyBySource;
        private readonly IReadOnlyDictionary<string, string> _locationDataUrlBySource;

        public BusLocationTrackingService(IConfiguration configuration, TimeService timeService, IHttpClientFactory httpClientFactory, TransportDataStore transportDataStore, ILogger<BusLocationTrackingService> logger)
        {
            _timeService = timeService;
            _httpClientFactory = httpClientFactory;
            _transportDataStore = transportDataStore;
            _logger = logger;

            _apiKeyBySource = configuration
                .GetSection("ApiKey")
                .Get<Dictionary<string, string>>() ?? throw new InvalidDataException("ApiKey");

            _locationDataUrlBySource = configuration
                .GetSection("Bus")
                .GetSection("LocationData")
                .Get<Dictionary<string, string>>() ?? throw new InvalidDataException("Bus:LocationData");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Dictionary<string, BusLocation> busLocations = [];

                try
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    foreach ((string source, string locationDataUrl) in _locationDataUrlBySource)
                    {
                        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, locationDataUrl);

                        HttpClient client = _httpClientFactory.CreateClient();
                        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

                        response.EnsureSuccessStatusCode();

                        using MemoryStream stream = new MemoryStream();
                        using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                        await responseStream.CopyToAsync(stream, cancellationToken);
                        stream.Position = 0;

                        await stream.ProcessXmlStreamsAsync(
                            async (xmlStream, cancellationToken) =>
                            {
                                Dictionary<string, BusLocation> entryBusLocations = await xmlStream.ParseBusLocation(_siriNamespace, _unknownPlaceholder, _timeService.UkTimeZone, _logger, cancellationToken);
                                foreach (var (key, value) in entryBusLocations)
                                    busLocations[key] = value;   // last-wins on duplicate keys
                            },
                            cancellationToken
                        );
                    }

                    await _transportDataStore.RefreshBusLocations(busLocations.ToFrozenDictionary());
                    _logger.LogInformation("Bus location tracking completed in {Elapsed}s", stopwatch.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download and import bus locations from zip archive");
                }

                await Task.Delay(10000, cancellationToken);
            }
        }
    }
}