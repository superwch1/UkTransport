using Backend.Extensions;
using Backend.Models;
using Backend.Repositories;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace Backend.Services
{
    public class BusTimetableImportService : BackgroundService
    {
        private static readonly XNamespace _transXChangeNamespace = "http://www.transxchange.org.uk/";

        private const int PageSize = 1000;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly TimeService _timeService;
        private readonly TransportDataStore _transportDataStore;

        private readonly ILogger<BusTimetableImportService> _logger;

        private readonly TimetableMetaOptions _meta;
        private readonly IReadOnlyDictionary<string, string> _apiKeyBySource;
        private readonly IReadOnlyDictionary<string, TimetableSourceOptions> _sourceOptionsBySource;

        public BusTimetableImportService(IHttpClientFactory httpClientFactory, IConfiguration configuration, IServiceScopeFactory serviceScopeFactory, TimeService timeService, TransportDataStore transportDataStore, ILogger<BusTimetableImportService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _serviceScopeFactory = serviceScopeFactory;
            _timeService = timeService;
            _transportDataStore = transportDataStore;
            _logger = logger;

            _apiKeyBySource = configuration
                .GetSection("ApiKey")
                .Get<Dictionary<string, string>>() ?? throw new InvalidDataException("ApiKey");

            _meta = configuration
                .GetSection("Bus")
                .GetSection("Timetable")
                .GetSection("Meta")
                .Get<TimetableMetaOptions>() ?? throw new InvalidDataException("Bus:Timetable:Meta");

            _sourceOptionsBySource = configuration
                .GetSection("Bus")
                .GetSection("Timetable")
                .GetSection("Sources")
                .Get<Dictionary<string, TimetableSourceOptions>>() ?? throw new InvalidDataException("Bus:Timetable:Sources");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // early return since the bus stop is not finished imported yet
                using (IServiceScope stopScope = _serviceScopeFactory.CreateScope())
                {
                    StopRepository stopRepository = stopScope.ServiceProvider.GetRequiredService<StopRepository>();
                    if (!stopRepository.IsStopFinishedImport())
                    {
                        await Task.Delay(1000);
                        continue;
                    }
                }

                // Built from what is already stored before anything is downloaded, since the import runs for hours
                await RefreshBusRoutes();

                await ImportBodsTimetables(cancellationToken);
                await ImportTflTimetables(cancellationToken);

                await RefreshBusRoutes();


                DateTime ukNow = _timeService.UkNowDateTime;
                DateTime nextRun = ukNow.Date.Add(_meta.DailyRunTime);

                if (nextRun <= ukNow)
                    nextRun = nextRun.AddDays(1);

                TimeSpan untilNextRun = nextRun - ukNow;
                await Task.Delay(untilNextRun, cancellationToken);
            }
        }

        private async Task RefreshBusRoutes()
        {
            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                using IServiceScope scope = _serviceScopeFactory.CreateScope();
                BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();

                ImmutableArray<BusRoute> busRoutes = await busRepository.GetBusRoutes();
                _transportDataStore.RefreshBusRoutes(busRoutes);
                _logger.LogInformation("Bus location refresh completed in {Elapsed}s. {Routes} routes", stopwatch.Elapsed.TotalSeconds, busRoutes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fail to refresh bus routes");
            }
        }

        private async Task ImportBodsTimetables(CancellationToken cancellationToken)
        {
            const string source = "BODS";
            try
            {
                TimetableSourceOptions sourceOptions = GetTimetableSourceOptions(source);
                string apiKey = _apiKeyBySource[source] ?? throw new InvalidDataException($"ApiKey:{source}");
                string catalogueUrl = sourceOptions.CatalogueUrl ?? throw new InvalidDataException($"Bus:Timetable:Sources:{source}:CatalogueUrl");

                IReadOnlyList<string> sourceDatasetIds = await GetBodsDatasetIds(catalogueUrl, apiKey, cancellationToken); // Bee network ["12739", "12769", "14241", "14928", "16596", "17472"]; 
                foreach (string sourceDatasetId in sourceDatasetIds)
                {
                    string url = $"{string.Format(sourceOptions.Url, sourceDatasetId)}?api_key={apiKey}";
                    await ImportTimetables(source, sourceDatasetId, url, entryNameContains: null, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fail to import bus timetable from {Source}", source);
            }
        }

        private async Task ImportTflTimetables(CancellationToken cancellationToken)
        {
            const string source = "TFL";
            try
            {
                TimetableSourceOptions sourceOptions = GetTimetableSourceOptions(source);
                string sourceDatasetId = Path.GetFileNameWithoutExtension(new Uri(sourceOptions.Url).AbsolutePath);

                await ImportTimetables(source, sourceDatasetId, sourceOptions.Url, sourceOptions.EntryNameContains, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fail to import bus timetable from {Source}", source);
            }
        }

        private async Task ImportTimetables(string source, string sourceDatasetId, string url, string? entryNameContains, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();

            string datasetId = BusTimeTableExtension.BuildDatasetKey(source, sourceDatasetId);
            using (IServiceScope freshnessScope = _serviceScopeFactory.CreateScope())
            {
                BusRepository busRepository = freshnessScope.ServiceProvider.GetRequiredService<BusRepository>();
                BusDataset? importedDataset = await busRepository.GetBusDataset(datasetId);

                if (importedDataset is not null && _timeService.UtcNowDateTimeOffset - importedDataset.ImportedAt < _meta.DatasetRefreshInterval)
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
                using (IServiceScope datasetScope = _serviceScopeFactory.CreateScope())
                {
                    BusRepository busRepository = datasetScope.ServiceProvider.GetRequiredService<BusRepository>();
                    await busRepository.ResetBusDataset(new BusDataset { Id = datasetId, ImportedAt = _timeService.UtcNowDateTimeOffset });
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
                        using IServiceScope scope = this._serviceScopeFactory.CreateScope();
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
                throw new InvalidDataException($"Bus:Timetable:Sources:{source}");

            return sourceOptions;
        }


        // Bus:Timetable:Meta.
        public sealed record TimetableMetaOptions
        {
            // Time of day (UK time), the daily import starts.
            public required TimeSpan DailyRunTime { get; init; }

            // How long an already imported dataset is left alone before it is downloaded again.
            public required TimeSpan DatasetRefreshInterval { get; init; }
        }


        // One entry under Bus:Timetable:Sources.
        public sealed record TimetableSourceOptions
        {
            public required string Url { get; init; }

            public string? CatalogueUrl { get; init; }

            public string? EntryNameContains { get; init; }
        }
    }
}
