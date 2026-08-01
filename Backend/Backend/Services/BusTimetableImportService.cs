using Backend.Extensions;
using Backend.Models;
using Backend.Repositories;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace Backend.Services
{
    public class BusTimetableImportService : BackgroundService
    {
        private const string BodsSource = "BODS";
        private const string TflSource = "TFL";

        private static readonly XNamespace _transXChangeNamespace = "http://www.transxchange.org.uk/";

        private const int PageSize = 1000;

        // Timetables change slowly, so this runs once a day, just after midnight. The day filter turns over at
        // midnight, so importing straight afterwards puts the new day's journeys in before anything asks for them.
        private static readonly TimeSpan _dailyRunTime = new TimeSpan(0, 5, 0);

        // How stale a dataset already imported has to be before it is downloaded and rebuilt again.
        private static readonly TimeSpan _datasetRefreshInterval = TimeSpan.FromDays(7);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeService _timeService;
        private readonly ILogger<BusTimetableImportService> _logger;

        private readonly IReadOnlyDictionary<string, string> _apiKeyBySource;
        private readonly IReadOnlyDictionary<string, TimetableSourceOptions> _sourceOptionsBySource;

        public BusTimetableImportService(IHttpClientFactory httpClientFactory, IConfiguration configuration, IServiceScopeFactory scopeFactory, TimeService timeService, ILogger<BusTimetableImportService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
            _timeService = timeService;
            _logger = logger;

            _apiKeyBySource = configuration
                .GetSection("ApiKey")
                .Get<Dictionary<string, string>>() ?? throw new InvalidDataException("ApiKey");

            _sourceOptionsBySource = configuration
                .GetSection("Bus")
                .GetSection("TimetableData")
                .Get<Dictionary<string, TimetableSourceOptions>>() ?? throw new InvalidDataException("Bus:TimetableData");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Built from what is already stored before anything is downloaded, since the import runs for hours and
                // yesterday's routes are worth having in the meantime, then again once the new data has landed.
                await RefreshBusRoutes();

                try
                {
                    await ImportBods(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fail to import bus timetable from {Source}", BodsSource);
                }

                try
                {
                    await ImportTfl(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fail to import bus timetable from {Source}", TflSource);
                }

                await RefreshBusRoutes();

                await Task.Delay(GetDelayUntilDailyRun(), cancellationToken);
            }
        }

        // Swallows its own failures, so a bad read here never stops the import that follows it.
        private async Task RefreshBusRoutes()
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();

                IReadOnlyDictionary<string, IReadOnlyList<BusRoute>> busRoutesByLineName = await busRepository.GetBusOriginDestinations();

                _logger.LogInformation(
                    "Bus routes - {Lines} lines, {Routes} origin destination pairs",
                    busRoutesByLineName.Count, busRoutesByLineName.Sum(x => x.Value.Count));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fail to refresh bus routes");
            }
        }

        private TimeSpan GetDelayUntilDailyRun()
        {
            DateTime ukNow = _timeService.UkNowDateTime;
            DateTime nextRun = ukNow.Date.Add(_dailyRunTime);

            if (nextRun <= ukNow)
                nextRun = nextRun.AddDays(1);

            return nextRun - ukNow;
        }

        private async Task ImportBods(CancellationToken cancellationToken)
        {
            TimetableSourceOptions sourceOptions = GetTimetableSourceOptions(BodsSource);
            string apiKey = GetApiKey(BodsSource);
            string catalogueUrl = sourceOptions.CatalogueUrl ?? throw new InvalidDataException($"Bus:TimetableData:{BodsSource}:CatalogueUrl");

            IReadOnlyList<string> sourceDatasetIds = await GetBodsDatasetIds(catalogueUrl, apiKey, cancellationToken);
            foreach (string sourceDatasetId in sourceDatasetIds)
            {
                string url = $"{string.Format(sourceOptions.Url, sourceDatasetId)}?api_key={apiKey}";
                await ImportDataset(BodsSource, sourceDatasetId, url, entryNameContains: null, cancellationToken);
            }
        }

        private async Task ImportTfl(CancellationToken cancellationToken)
        {
            TimetableSourceOptions sourceOptions = GetTimetableSourceOptions(TflSource);
            string sourceDatasetId = Path.GetFileNameWithoutExtension(new Uri(sourceOptions.Url).AbsolutePath);

            await ImportDataset(TflSource, sourceDatasetId, sourceOptions.Url, sourceOptions.EntryNameContains, cancellationToken);
        }

        private async Task ImportDataset(string source, string sourceDatasetId, string url, string? entryNameContains, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();

            string datasetId = BusTimeTableExtension.BuildDatasetKey(source, sourceDatasetId);
            using (IServiceScope freshnessScope = _scopeFactory.CreateScope())
            {
                BusRepository busRepository = freshnessScope.ServiceProvider.GetRequiredService<BusRepository>();
                BusDataset? importedDataset = await busRepository.GetBusDataset(datasetId);

                if (importedDataset is not null && _timeService.UtcNowDateTimeOffset - importedDataset.ImportedAt < _datasetRefreshInterval)
                {
                    _logger.LogInformation("DatasetId ({DatasetId}) - skipped, imported at {ImportedAt}", datasetId, importedDataset.ImportedAt);
                    return;
                }
            }

            HttpClient client = _httpClientFactory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            try
            {
                // Cleared only once the download has come back, so a source that is briefly unreachable leaves the
                // timetables already held for it alone.
                using (IServiceScope datasetScope = _scopeFactory.CreateScope())
                {
                    BusRepository busRepository = datasetScope.ServiceProvider.GetRequiredService<BusRepository>();
                    await busRepository.ResetBusDataset(new BusDataset
                    {
                        Id = datasetId,
                        ImportedAt = _timeService.UtcNowDateTimeOffset,
                    });
                }

                using MemoryStream stream = new MemoryStream();
                using (Stream downloaded = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    await downloaded.CopyToAsync(stream, cancellationToken);
                }
                stream.Position = 0;

                await stream.ExtractXmlStreamsAsync(
                    async (xmlStream, cancellationToken) =>
                    {
                        using IServiceScope scope = _scopeFactory.CreateScope();
                        BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();

                        IReadOnlyList<BusTimetable> busTimetables = await xmlStream.ParseBusTimetable(_transXChangeNamespace, datasetId, _timeService.UkNowDateTime, _logger, cancellationToken);
                        await busRepository.BulkInsertBusTimetables(busTimetables);
                    },
                    cancellationToken,
                    entryNameContains
                );
                _logger.LogInformation("DatasetId ({DatasetId}) - Bus timetables import takes {Elapsed}s", datasetId, stopwatch.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fail to extract xml stream from {DatasetId}", datasetId);
            }
        }

        private async Task<IReadOnlyList<string>> GetBodsDatasetIds(string catalogueUrl, string apiKey, CancellationToken cancellationToken)
        {
            HttpClient client = _httpClientFactory.CreateClient();

            List<string> datasetIds = [];
            int offset = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string url = $"{catalogueUrl}?api_key={apiKey}&status=published&limit={PageSize}&offset={offset}";

                using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!document.RootElement.TryGetProperty("results", out JsonElement results) || results.GetArrayLength() == 0)
                    break;

                foreach (JsonElement dataset in results.EnumerateArray())
                {
                    if (dataset.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt32(out int datasetId))
                    {
                        datasetIds.Add(datasetId.ToString());
                    }
                }

                offset += PageSize;
            }

            return datasetIds;
        }

        private TimetableSourceOptions GetTimetableSourceOptions(string source)
        {
            if (!_sourceOptionsBySource.TryGetValue(source, out TimetableSourceOptions? sourceOptions) || string.IsNullOrWhiteSpace(sourceOptions.Url))
                throw new InvalidDataException($"Bus:TimetableData:{source}");

            return sourceOptions;
        }

        private string GetApiKey(string source)
        {
            if (!_apiKeyBySource.TryGetValue(source, out string? apiKey) || string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidDataException($"ApiKey:{source}");

            return apiKey;
        }

        // One entry under Bus:TimetableData.
        public sealed record TimetableSourceOptions
        {
            public required string Url { get; init; }

            public string? CatalogueUrl { get; init; }

            public string? EntryNameContains { get; init; }
        }
    }
}
