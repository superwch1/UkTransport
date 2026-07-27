using Backend.Extensions;
using Backend.Models;
using System.Text.Json;
using System.Xml.Linq;

namespace Backend.Services
{
    public class BusTimetableImportService : BackgroundService
    {
        private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "log.txt");

        private const string DatasetApiUrl = "https://data.bus-data.dft.gov.uk/api/v1/dataset/";
        private const string DownloadUrlFormat = "https://data.bus-data.dft.gov.uk/timetable/dataset/{0}/download/";
        private static readonly XNamespace _transXChangeNamespace = "http://www.transxchange.org.uk/";

        private const int PageSize = 1000;

        // pause between downloads so we don't flood the server.
        private static readonly TimeSpan DelayBetweenDownloads = TimeSpan.FromSeconds(1);

        // Timetables change slowly; re-run once a day.
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeService _timeService;
        private readonly ILogger<BusTimetableImportService> _logger;

        private readonly IReadOnlyDictionary<string, string> _apiKeyBySource;
        private readonly IReadOnlyDictionary<string, string> _timetableDataUrlBySource;

        public BusTimetableImportService(IHttpClientFactory httpClientFactory, IConfiguration configuration, IServiceScopeFactory scopeFactory, TimeService timeService, ILogger<BusTimetableImportService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
            _timeService = timeService;
            _logger = logger;

            _apiKeyBySource = configuration
                .GetSection("ApiKey")
                .Get<Dictionary<string, string>>() ?? throw new InvalidDataException("ApiKey");

            _timetableDataUrlBySource = configuration
                .GetSection("Bus")
                .GetSection("LocationData")
                .Get<Dictionary<string, string>>() ?? throw new InvalidDataException("Bus:TimetableData");
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

                    string url = $"{DatasetApiUrl}?api_key={_apiKeyBySource["BODS"]}&status=published&limit={PageSize}&offset={offset}";

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

        private async Task DownloadDataset(int datasetId, CancellationToken cancellationToken)
        {
            string url = string.Format(DownloadUrlFormat, datasetId);
            _logger.LogInformation($"working on {url}");

            try
            {
                HttpClient client = _httpClientFactory.CreateClient();

                LogMessage(url);

                using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                using MemoryStream stream = new MemoryStream();
                using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    await source.CopyToAsync(stream, cancellationToken);
                }
                stream.Position = 0;

                await stream.ExtractXmlStreamsAsync(
                    async (xmlStream, cancellationToken) =>
                    {
                        IReadOnlyList<BusTimetable> entryBusLocations = await xmlStream.ParseBusTimetable(_transXChangeNamespace, _timeService.UkNowDateTime, _logger, cancellationToken);

                    },
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                LogException(ex.Message);
            }
        }


        private static void LogMessage(string message)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, entry);
        }

        private static void LogException(string exceptionMessage)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Environment.NewLine}{exceptionMessage}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(LogPath, entry);
        }
    }
}