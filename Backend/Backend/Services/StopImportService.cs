using Backend.Enumerations;
using Backend.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Diagnostics;
using System.Globalization;

namespace Backend.Services
{
    public class StopImportService : BackgroundService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TransportDataStore _transportDataStore;
        private readonly ILogger<StopImportService> _logger;
        private readonly string _naptanCsvUrl;
        private readonly IReadOnlyList<string> _busStopTypes;

        // Compass bearing -> degrees. Anything missing/unknown defaults to N (0).
        private static readonly Dictionary<string, int> BearingDegrees = new(StringComparer.OrdinalIgnoreCase)
        { ["N"] = 0, ["NE"] = 45, ["E"] = 90, ["SE"] = 135, ["S"] = 180, ["SW"] = 225, ["W"] = 270, ["NW"] = 315 };

        public StopImportService(IHttpClientFactory httpClientFactory, TransportDataStore transportDataStore, ILogger<StopImportService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _transportDataStore = transportDataStore;
            _naptanCsvUrl = configuration
                .GetSection("Stop")
                .GetSection("NaptanData")
                .Get<string>() ?? throw new InvalidDataException("Stop:NaptanData");

            _busStopTypes = configuration
                .GetSection("Stop")
                .GetSection("BusTypes")
                .Get<IReadOnlyList<string>>() ?? throw new InvalidDataException("Stop:BusTypes");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while(!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    HttpClient client = _httpClientFactory.CreateClient();

                    using HttpResponseMessage response = await client.GetAsync(_naptanCsvUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using StreamReader reader = new StreamReader(stream);

                    CsvConfiguration config = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        MissingFieldFound = null,
                        BadDataFound = null,
                    };

                    using CsvReader csv = new CsvReader(reader, config);
                    await csv.ReadAsync();
                    csv.ReadHeader();

                    Dictionary<string, Stop> busStops = new Dictionary<string, Stop>();
                    while (await csv.ReadAsync())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string? type = csv.GetField("StopType");
                        if (type is null)
                            continue;

                        StopType stopType;
                        if (_busStopTypes.Any(x => string.Equals(x, type, StringComparison.OrdinalIgnoreCase)))
                            stopType = StopType.Bus;
                        else
                            continue;

                        string? status = csv.GetField("Status");
                        if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string? id = csv.GetField("ATCOCode")?.Trim();
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        if (!decimal.TryParse(csv.GetField("Latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal latitude) ||
                            !decimal.TryParse(csv.GetField("Longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal longitude))
                            continue;

                        string? bearingRaw = csv.GetField("Bearing")?.Trim();
                        int bearing = bearingRaw is not null && BearingDegrees.TryGetValue(bearingRaw, out int degree)
                            ? degree
                            : 0; // default: N

                        busStops[id] = new Stop()
                        {
                            Id = id,
                            CommonName = csv.GetField("CommonName")?.Trim() ?? "",
                            Bearing = bearing,
                            Latitude = latitude,
                            Longitude = longitude,
                            StopType = stopType
                        };
                    }

                    _transportDataStore.RefreshStops(busStops);
                    _logger.LogInformation("Stop import completed in {Elapsed}s", stopwatch.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download and import bus locations from csv file");
                }

                await Task.Delay(TimeSpan.FromHours(24), cancellationToken);
            }
        }
    }
}
