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
        private static readonly XNamespace _siriNamespace = "http://www.siri.org.uk/siri";

        private readonly TimeService _timeService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TransportDataStore _transportDataStore;
        private readonly ILogger<BusLocationTrackingService> _logger;

        private readonly LocationMetaOptions _meta;
        private readonly IReadOnlyDictionary<string, string> _locationUrlBySource;

        public BusLocationTrackingService(IConfiguration configuration, TimeService timeService, IHttpClientFactory httpClientFactory, TransportDataStore transportDataStore, ILogger<BusLocationTrackingService> logger)
        {
            _timeService = timeService;
            _httpClientFactory = httpClientFactory;
            _transportDataStore = transportDataStore;
            _logger = logger;

            _meta = configuration
                .GetSection("Bus")
                .GetSection("Location")
                .GetSection("Meta")
                .Get<LocationMetaOptions>() ?? throw new InvalidDataException("Bus:Location:Meta");

            _locationUrlBySource = configuration
                .GetSection("Bus")
                .GetSection("Location")
                .GetSection("Sources")
                .Get<Dictionary<string, string>>() ?? throw new InvalidDataException("Bus:Location:Sources");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Dictionary<string, BusLocation> busLocations = [];

                try
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    foreach ((string source, string locationUrl) in _locationUrlBySource)
                    {
                        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, locationUrl);

                        HttpClient client = _httpClientFactory.CreateClient();
                        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

                        response.EnsureSuccessStatusCode();

                        using MemoryStream stream = new MemoryStream();
                        using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                        await responseStream.CopyToAsync(stream, cancellationToken);
                        stream.Position = 0;

                        await stream.ExtractXmlStreamsAsync(
                            async (xmlStream, cancellationToken) =>
                            {
                                Dictionary<string, BusLocation> entryBusLocations = await xmlStream.ParseBusLocation(_siriNamespace, _timeService.UkNowDateTime, _meta.RetentionPeriod, _timeService.UkTimeZone, _logger, cancellationToken);
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

                await Task.Delay(_meta.RefreshInterval, cancellationToken);
            }
        }


        public sealed record LocationMetaOptions
        {
            public required TimeSpan RefreshInterval { get; init; }

            public required TimeSpan RetentionPeriod { get; init; }
        }
    }
}