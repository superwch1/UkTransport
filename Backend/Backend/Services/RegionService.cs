using Backend.Enumerations;
using Backend.Extensions;
using System.Text.Json;

namespace Backend.Services
{
    // https://www.data.gov.uk/dataset/1db83d90-b734-472a-9d14-6f3ae7a0bdf0/international-territorial-level-1-january-2021-boundaries-uk-buc
    public class RegionService
    {
        private const string BoundaryFileName = "ITL1UK.geojson";

        private readonly IReadOnlyList<RegionBoundary> _regionBoundaries;
        private readonly ILogger<RegionService> _logger;

        public RegionService(ILogger<RegionService> logger)
        {
            _logger = logger;
            _regionBoundaries = LoadBoundaries();

            _logger.LogInformation("Loaded {Count} ITL1 region boundaries", _regionBoundaries.Count);
        }


        public Itl1Region GetRegion(decimal latitude, decimal longitude)
        {
            double longitudeDegrees = (double)longitude;
            double latitudeDegrees = (double)latitude;

            foreach (RegionBoundary regionBoundary in _regionBoundaries)
            {
                if (regionBoundary.Contains(longitudeDegrees, latitudeDegrees))
                    return regionBoundary.Region;
            }

            return Itl1Region.None;
        }


        private IReadOnlyList<RegionBoundary> LoadBoundaries()
        {
            string path = Path.Combine(AppContext.BaseDirectory, BoundaryFileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"{BoundaryFileName} not found", path);

            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);

            List<RegionBoundary> boundaries = [];
            foreach (JsonElement feature in document.RootElement.GetProperty("features").EnumerateArray())
            {
                string? code = feature.GetProperty("properties").GetProperty("ITL121CD").GetString();
                Itl1Region region = ParseRegionCode(code);
                if (region == Itl1Region.None)
                {
                    throw new InvalidDataException($"Skipped ITL1 boundary with unrecognised code {code}");
                }

                JsonElement geometry = feature.GetProperty("geometry");
                string geometryType = geometry.GetProperty("type").GetString() ?? "";
                JsonElement coordinates = geometry.GetProperty("coordinates");

                // A Polygon is one list of rings; a MultiPolygon is a list of those. Wrapping the first lets both be
                // walked the same way below.
                IEnumerable<JsonElement> polygons = geometryType == "MultiPolygon"
                    ? coordinates.EnumerateArray()
                    : [coordinates];

                List<Ring[]> parsedPolygons = [];
                foreach (JsonElement polygon in polygons)
                {
                    Ring[] rings = polygon.EnumerateArray().Select(ring => ParseRing(ring, region)).ToArray();
                    if (rings.Length > 0)
                        parsedPolygons.Add(rings);
                }

                boundaries.Add(new RegionBoundary(region, parsedPolygons));
            }

            return boundaries;
        }


        // Converts the ring off the grid as it is read, so nothing downstream deals in eastings and northings.
        private static Ring ParseRing(JsonElement ring, Itl1Region region)
        {
            List<double> longitudes = [];
            List<double> latitudes = [];

            foreach (JsonElement point in ring.EnumerateArray())
            {
                double easting = point[0].GetDouble();
                double northing = point[1].GetDouble();

                // A vertex that will not convert means the file is not on the grid it claims to be, which is worth
                // failing the startup over rather than quietly drawing a region with a corner missing.
                if (!OsGridReference.TryConvertToWgs84(easting, northing, out decimal latitude, out decimal longitude))
                    throw new InvalidDataException($"{BoundaryFileName} has a vertex outside the British National Grid for {region}: {easting}, {northing}");

                longitudes.Add((double)longitude);
                latitudes.Add((double)latitude);
            }

            return new Ring([.. longitudes], [.. latitudes]);
        }


        private static Itl1Region ParseRegionCode(string? code)
        {
            return code switch
            {
                "TLC" => Itl1Region.NorthEast,
                "TLD" => Itl1Region.NorthWest,
                "TLE" => Itl1Region.YorkshireAndTheHumber,
                "TLF" => Itl1Region.EastMidlands,
                "TLG" => Itl1Region.WestMidlands,
                "TLH" => Itl1Region.EastOfEngland,
                "TLI" => Itl1Region.London,
                "TLJ" => Itl1Region.SouthEast,
                "TLK" => Itl1Region.SouthWest,
                "TLL" => Itl1Region.Wales,
                "TLM" => Itl1Region.Scotland,
                "TLN" => Itl1Region.NorthernIreland,
                _ => Itl1Region.None,
            };
        }


        // One region, as the one or more polygons it is drawn with. Scotland alone is nearly two hundred of them,
        // almost all islands, so the region is rejected on its own bounding box before any polygon is looked at, and
        // each polygon is then rejected on its own before any edge is walked.
        private sealed class RegionBoundary
        {
            public Itl1Region Region { get; }

            private readonly IReadOnlyList<Ring[]> _polygons;
            private readonly BoundingBox _boundingBox;

            public RegionBoundary(Itl1Region region, IReadOnlyList<Ring[]> polygons)
            {
                Region = region;
                _polygons = polygons;
                _boundingBox = BoundingBox.Around(polygons.Select(polygon => polygon[0].BoundingBox));
            }

            public bool Contains(double longitude, double latitude)
            {
                if (!_boundingBox.Contains(longitude, latitude))
                    return false;

                foreach (Ring[] polygon in _polygons)
                {
                    // The first ring is the outline and any that follow are holes. Counting crossings over all of
                    // them together handles the holes for free: a point inside one crosses the outline and the hole,
                    // an even number of times, and so falls outside.
                    if (!polygon[0].BoundingBox.Contains(longitude, latitude))
                        continue;

                    int crossedRings = 0;
                    foreach (Ring ring in polygon)
                    {
                        if (ring.Crosses(longitude, latitude))
                            crossedRings++;
                    }

                    if (crossedRings % 2 == 1)
                        return true;
                }

                return false;
            }
        }


        private readonly record struct BoundingBox(double MinimumLongitude, double MaximumLongitude, double MinimumLatitude, double MaximumLatitude)
        {
            public bool Contains(double longitude, double latitude)
            {
                return longitude >= MinimumLongitude
                    && longitude <= MaximumLongitude
                    && latitude >= MinimumLatitude
                    && latitude <= MaximumLatitude;
            }

            public static BoundingBox Around(IEnumerable<BoundingBox> boundingBoxes)
            {
                double minimumLongitude = double.MaxValue;
                double maximumLongitude = double.MinValue;
                double minimumLatitude = double.MaxValue;
                double maximumLatitude = double.MinValue;

                foreach (BoundingBox boundingBox in boundingBoxes)
                {
                    minimumLongitude = Math.Min(minimumLongitude, boundingBox.MinimumLongitude);
                    maximumLongitude = Math.Max(maximumLongitude, boundingBox.MaximumLongitude);
                    minimumLatitude = Math.Min(minimumLatitude, boundingBox.MinimumLatitude);
                    maximumLatitude = Math.Max(maximumLatitude, boundingBox.MaximumLatitude);
                }

                return new BoundingBox(minimumLongitude, maximumLongitude, minimumLatitude, maximumLatitude);
            }
        }


        // Held as two arrays of coordinates rather than a list of points, so walking the edges stays a linear read
        // over the values themselves.
        private sealed class Ring
        {
            private readonly double[] _longitudes;
            private readonly double[] _latitudes;

            public BoundingBox BoundingBox { get; }

            public Ring(double[] longitudes, double[] latitudes)
            {
                _longitudes = longitudes;
                _latitudes = latitudes;

                BoundingBox = longitudes.Length == 0
                    ? new BoundingBox(0, 0, 0, 0)
                    : new BoundingBox(longitudes.Min(), longitudes.Max(), latitudes.Min(), latitudes.Max());
            }

            // Ray casting: counts how often a ray running east from the point crosses this ring. The half-open test
            // on latitude means a vertex sitting exactly on the ray is counted once rather than twice.
            public bool Crosses(double longitude, double latitude)
            {
                bool inside = false;

                for (int i = 0, j = _longitudes.Length - 1; i < _longitudes.Length; j = i++)
                {
                    if ((_latitudes[i] > latitude) == (_latitudes[j] > latitude))
                        continue;

                    double crossingLongitude = _longitudes[i]
                        + (latitude - _latitudes[i]) / (_latitudes[j] - _latitudes[i]) * (_longitudes[j] - _longitudes[i]);

                    if (longitude < crossingLongitude)
                        inside = !inside;
                }

                return inside;
            }
        }
    }
}
