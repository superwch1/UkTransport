using Backend.Models;
using System.Xml.Linq;

namespace Backend.Extensions
{
    public static class ParserExtension
    {
        public static async Task<Dictionary<string, BusLocation>> ParseBusLocation(this Stream stream, XNamespace xmlNamespace, string unknownPlaceholder, TimeZoneInfo timeZoneInfo, ILogger logger, CancellationToken cancellationToken)
        {
            Dictionary<string, BusLocation> busLocations = [];
            XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            IEnumerable<XElement> activities = document.Descendants(xmlNamespace + "VehicleActivity");
            foreach (XElement activity in activities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    DateTime? recordedAtTime = activity.Value(xmlNamespace, "RecordedAtTime").ParseUkDateTime(timeZoneInfo);
                    if (recordedAtTime is null || DateTime.Now - TimeSpan.FromMinutes(10) > recordedAtTime.Value)
                        continue;

                    XElement journey = activity.Element(xmlNamespace + "MonitoredVehicleJourney") ?? throw new InvalidDataException("<MonitoredVehicleJourney> element not found.");

                    string? operatorRef = journey.Value(xmlNamespace, "OperatorRef");
                    string? publishedLineName = journey.Value(xmlNamespace, "PublishedLineName");

                    string? originRef = journey.Value(xmlNamespace, "OriginRef");
                    string? destinationRef = journey.Value(xmlNamespace, "DestinationRef");

                    // possible value: inbound, outbound, clockwise, anticlockwise, 1, 2
                    string? directionRef = journey.Value(xmlNamespace, "DirectionRef");

                    if (operatorRef is null || publishedLineName is null || originRef is null || destinationRef is null || directionRef is null)
                        continue;

                    string originName = journey.Value(xmlNamespace, "OriginName") ?? unknownPlaceholder;
                    string destinationName = journey.Value(xmlNamespace, "DestinationName") ?? unknownPlaceholder;

                    TimeOnly? originAimedDepartureTime = journey.Value(xmlNamespace, "OriginAimedDepartureTime").ParseUkTimeOnly(timeZoneInfo);
                    TimeOnly? destinationAimedArrivalTime = journey.Value(xmlNamespace, "DestinationAimedArrivalTime").ParseUkTimeOnly(timeZoneInfo);

                    string? journeyCode = activity
                        .Element(xmlNamespace + "Extensions")?
                        .Element(xmlNamespace + "Operational")?
                        .Element(xmlNamespace + "TicketMachine")?
                        .Value(xmlNamespace, "JourneyCode");

                    TimeOnly? departureTimeFromJourneyCode = (journeyCode is null || journeyCode == "0000")
                        ? null
                        : journeyCode.ParseTimeOnly(format: ["HHmm", "HH:mm:ss"]); // "0000" is that machine's null sentinel

                    // seems the bus either do not have arrival time or both arrival and departure time
                    originAimedDepartureTime = originAimedDepartureTime ?? departureTimeFromJourneyCode;
                    if (!originAimedDepartureTime.HasValue)
                        continue;

                    XElement? location = journey?.Element(xmlNamespace + "VehicleLocation");
                    decimal? latitude = location.Value(xmlNamespace, "Latitude").ParseDecimal();
                    decimal? longitude = location.Value(xmlNamespace, "Longitude").ParseDecimal();
                    decimal bearing = journey.Value(xmlNamespace, "Bearing").ParseDecimal() ?? 0;

                    if (latitude is null || longitude is null)
                        continue;

                    string originDepartureKey = BusTimeTableExtension.CreateOriginDepartureKey(originAimedDepartureTime.Value, originRef, destinationRef);
                    busLocations[originDepartureKey] = new BusLocation
                    {
                        OriginDepartureKey = originDepartureKey,
                        RecordedAtTime = recordedAtTime.Value,

                        OperatorRef = operatorRef,
                        PublishedLineName = publishedLineName,
                        DirectionRef = directionRef,

                        OriginName = originName,
                        OriginRef = originRef,
                        OriginAimedDepartureTime = originAimedDepartureTime.Value,

                        DestinationName = destinationName,
                        DestinationRef = destinationRef,
                        DestinationAimedArrivalTime = destinationAimedArrivalTime,

                        Latitude = latitude.Value,
                        Longitude = longitude.Value,
                        Bearing = bearing
                    };
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to import bus locations from {Activity}", activity.Value);
                }
            }

            return busLocations;
        }
    }
}
