using Backend.Enumerations;
using Backend.Models;
using CsvHelper;
using CsvHelper.Configuration;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
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
        private static readonly Dictionary<string, int> BearingDegrees = new(StringComparer.OrdinalIgnoreCase) { ["N"] = 0, ["NE"] = 45, ["E"] = 90, ["SE"] = 135, ["S"] = 180, ["SW"] = 225, ["W"] = 270, ["NW"] = 315 };

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
                        {
                            if (!OsGridReference.TryConvertToWgs84(csv.GetField("GridType"), csv.GetField("Easting"), csv.GetField("Northing"), out latitude, out longitude))
                                continue;
                        }

                        string? bearingRaw = csv.GetField("Bearing")?.Trim();
                        int bearing = bearingRaw is not null && BearingDegrees.TryGetValue(bearingRaw, out int degree)
                            ? degree
                            : 0; // default: N

                        busStops[id] = new Stop()
                        {
                            Id = id,
                            Name = csv.GetField("CommonName")?.Trim() ?? "",
                            Bearing = bearing,
                            Latitude = latitude,
                            Longitude = longitude,
                            StopType = stopType
                        };
                    }

                    _transportDataStore.RefreshStops(busStops);
                    _logger.LogInformation("Stop import completed in {Elapsed}s ({Count} stops)", stopwatch.Elapsed.TotalSeconds, busStops.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download and import bus locations from csv file");
                }

                await Task.Delay(TimeSpan.FromHours(24), cancellationToken);
            }
        }
    }


    internal static class OsGridReference
    {
        // The British National Grid, together with the Helmert parameters EPSG publishes for the shift from
        // OSGB36 onto WGS84. The TOWGS84 line is the part worth guarding: without it the projection still
        // resolves, but every position lands about 100m from where it belongs because the datum never moves.
        private const string BritishNationalGridWkt = """
            PROJCS["OSGB36 / British National Grid",
              GEOGCS["OSGB36",
                DATUM["OSGB_1936",
                  SPHEROID["Airy 1830",6377563.396,299.3249646],
                  TOWGS84[446.448,-125.157,542.06,0.1502,0.247,0.8421,-20.4894]],
                PRIMEM["Greenwich",0],
                UNIT["degree",0.0174532925199433]],
              PROJECTION["Transverse_Mercator"],
              PARAMETER["latitude_of_origin",49],
              PARAMETER["central_meridian",-2],
              PARAMETER["scale_factor",0.9996012717],
              PARAMETER["false_easting",400000],
              PARAMETER["false_northing",-100000],
              UNIT["metre",1],
              AXIS["Easting",EAST],
              AXIS["Northing",NORTH]]
            """;

        // The bounding box the National Grid is defined over, give or take. A stop landing outside it came from
        // an easting and northing that were never valid, so the row is dropped rather than placed in the sea.
        private const decimal MinimumLatitude = 49.0m;
        private const decimal MaximumLatitude = 61.5m;
        private const decimal MinimumLongitude = -9.0m;
        private const decimal MaximumLongitude = 2.5m;

        private static readonly MathTransform s_gridToWgs84 = new CoordinateTransformationFactory()
            .CreateFromCoordinateSystems(
                new CoordinateSystemFactory().CreateFromWkt(BritishNationalGridWkt),
                GeographicCoordinateSystem.WGS84)
            .MathTransform;

        public static bool TryConvertToWgs84(string? gridType, string? easting, string? northing, out decimal latitude, out decimal longitude)
        {
            latitude = 0;
            longitude = 0;

            // An empty grid type means UKOS, which is what the schema defaults to. Anything else named, such as
            // the Irish grid used across Northern Ireland, needs different constants and is left alone.
            if (!string.IsNullOrWhiteSpace(gridType) && !string.Equals(gridType.Trim(), "UKOS", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!double.TryParse(easting, NumberStyles.Float, CultureInfo.InvariantCulture, out double eastingValue) ||
                !double.TryParse(northing, NumberStyles.Float, CultureInfo.InvariantCulture, out double northingValue))
                return false;

            (double longitudeDegrees, double latitudeDegrees) = s_gridToWgs84.Transform(eastingValue, northingValue);

            if (double.IsNaN(latitudeDegrees) || double.IsNaN(longitudeDegrees))
                return false;

            decimal convertedLatitude = (decimal)latitudeDegrees;
            decimal convertedLongitude = (decimal)longitudeDegrees;

            if (convertedLatitude < MinimumLatitude || convertedLatitude > MaximumLatitude ||
                convertedLongitude < MinimumLongitude || convertedLongitude > MaximumLongitude)
                return false;

            latitude = convertedLatitude;
            longitude = convertedLongitude;
            return true;
        }
    }
}
