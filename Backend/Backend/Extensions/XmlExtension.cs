using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Backend.Extensions
{
    public static class XmlExtension
    {
        // Recursively walks a zip (and any nested zips) and invokes the handler for every .xml entry found at any depth
        // a zip has no real folder hierarchy to traverse and no need to go over each folder
        public static async Task ExtractXmlStreamsAsync(this Stream stream, Func<Stream, CancellationToken, Task> processXmlStream, CancellationToken cancellationToken)
        {
            using MemoryStream buffered = new MemoryStream();
            await stream.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;

            // ZIP local file header magic: 'P' 'K' 0x03 0x04
            Span<byte> header = stackalloc byte[4];
            int read = buffered.Read(header);
            buffered.Position = 0;

            bool isZip = read == 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
            if (!isZip)
            {
                await processXmlStream(buffered, cancellationToken);
                return;
            }

            using ZipArchive archive = new ZipArchive(buffered);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.FullName.EndsWith('/'))   // directory entry
                    continue;

                bool isXml = entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
                bool isNestedZip = entry.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

                if (!isXml && !isNestedZip)
                    continue;

                using MemoryStream entryBuffered = new MemoryStream();
                using (Stream entryStream = await entry.OpenAsync(cancellationToken))
                {
                    await entryStream.CopyToAsync(entryBuffered, cancellationToken);
                }
                entryBuffered.Position = 0;
                await ExtractXmlStreamsAsync(entryBuffered, processXmlStream, cancellationToken);
            }
        }

        public static string? Value(this XElement? parent, XNamespace xNamespace, string elementName)
        {
            string? value = parent?.Element(xNamespace + elementName)?.Value;
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        /// <summary>
        /// Reads an attribute, trimmed. Ids are matched against element values read by
        /// <see cref="Value"/>, so both sides have to be trimmed the same way.
        /// </summary>
        public static string? AttributeValue(this XElement? element, string attributeName)
        {
            string? value = element?.Attribute(attributeName)?.Value;
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
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result)
                ? result
                : null;
        }

        public static TimeSpan ParseDuration(this string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TimeSpan.Zero;

            return XmlConvert.ToTimeSpan(value);
        }

    }
}
