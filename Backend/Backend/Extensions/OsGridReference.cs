using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using System.Globalization;

namespace Backend.Extensions
{
    // Converts between the British National Grid and WGS84, in both directions.
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

            return TryConvertToWgs84(eastingValue, northingValue, out latitude, out longitude);
        }


        public static bool TryConvertToWgs84(double easting, double northing, out decimal latitude, out decimal longitude)
        {
            latitude = 0;
            longitude = 0;

            (double longitudeDegrees, double latitudeDegrees) = s_gridToWgs84.Transform(easting, northing);

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
