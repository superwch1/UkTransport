using Backend.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace Backend.Services
{
    public class BusStopImportService : IHostedService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TransportDataStore _transportDataStore;

        private const string NaptanCsvUrl = "https://beta-naptan.dft.gov.uk/Download/National/csv";
        private static readonly HashSet<string> AllowedStopTypes = new(StringComparer.OrdinalIgnoreCase) { "BCT", "BCS", "BCQ" };

        // Compass bearing -> degrees. Anything missing/unknown defaults to N (0).
        private static readonly Dictionary<string, int> BearingDegrees = new(StringComparer.OrdinalIgnoreCase)
        { ["N"] = 0, ["NE"] = 45, ["E"] = 90, ["SE"] = 135, ["S"] = 180, ["SW"] = 225, ["W"] = 270, ["NW"] = 315 };

        public BusStopImportService(IHttpClientFactory httpClientFactory, TransportDataStore transportDataStore)
        {
            _httpClientFactory = httpClientFactory;
            _transportDataStore = transportDataStore;
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();

            // Stream the response so the whole file never sits in memory.
            using HttpResponseMessage response = await client.GetAsync(NaptanCsvUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            Dictionary<string, BusStop> busStops = new Dictionary<string, BusStop>();
            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? stopType = csv.GetField("StopType");
                if (stopType is null || !AllowedStopTypes.Contains(stopType))
                    continue;

                string? status = csv.GetField("Status");
                if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? id = csv.GetField("ATCOCode")?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (!decimal.TryParse(csv.GetField("Latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal lat) ||
                    !decimal.TryParse(csv.GetField("Longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal lon))
                    continue; // skip rows without usable coordinates

                string? bearingRaw = csv.GetField("Bearing")?.Trim();
                int bearing = bearingRaw is not null && BearingDegrees.TryGetValue(bearingRaw, out int deg)
                    ? deg
                    : 0; // default: N

                busStops[id] = new BusStop()
                {
                    Id = id,
                    CommonName = csv.GetField("CommonName")?.Trim() ?? "",
                    Bearing = bearing,
                    Latitude = lat,
                    Longitude = lon
                };
            }

            _transportDataStore.SetBusStops(busStops);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
