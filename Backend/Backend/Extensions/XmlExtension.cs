using System.Globalization;
using System.Xml.Linq;

namespace Backend.Extensions
{
    public static class XmlExtension
    {
        public static string? ExtensionValue(this XElement activity, XNamespace xNamespace, string localName)
        {
            XElement? extensions =
                activity.Element(xNamespace + "Extensions") ??
                activity.Element("Extensions");

            string? value = extensions?
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName == localName)?
                .Value
                .Trim();

            return string.IsNullOrEmpty(value) ? null : value;
        }

        public static string? Value(this XElement? parent, XNamespace xNamespace, string elementName)
        {
            string? value = parent?.Element(xNamespace + elementName)?.Value;
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        public static DateTime? ParseUkDateTime(this string? value, TimeZoneInfo timeZoneInfo)
        {
            return DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset result)
                    ? TimeZoneInfo.ConvertTime(result, timeZoneInfo).DateTime
                    : null;
        }

        public static TimeOnly? ParseUkTimeOnly(this string? value, TimeZoneInfo timeZoneInfo)
        {
            return DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset result)
                    ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(result, timeZoneInfo).DateTime)
                    : null;
        }

        public static TimeOnly? ParseTimeOnly(this string? value, string[]? format = null)
        {
            if (format == null)
            {
                return TimeOnly.TryParse(
                    value, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly result)
                        ? result
                        : null;
            }
            else
            {
                return TimeOnly.TryParseExact(
                    value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly result)
                        ? result
                        : null;
            }
        }

        public static DateOnly? ParseDateOnly(this string? value)
        {
            return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly result)
                ? result
                : null;
        }
    

        public static decimal? ParseDecimal(this string? value)
        {
            return decimal.TryParse(value, out decimal result)
                ? result
                : null;
        }
    }
}
