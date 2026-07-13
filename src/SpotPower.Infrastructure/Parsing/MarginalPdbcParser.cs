using System.Globalization;
using Microsoft.Extensions.Logging;
using SpotPower.Core.Entities;

namespace SpotPower.Infrastructure.Parsing;

public class MarginalPdbcParser
{
    // 15-minute MTU reform: SDAC (and therefore OMIE) moved from hourly to
    // quarter-hourly periods for trading day 30 Sept 2025 / delivery day
    // 1 Oct 2025. Files from that delivery date onward have up to ~96
    // periods/day instead of ~24.
    private static readonly DateOnly FifteenMinuteEraStart = new(2025, 10, 1);

    private static readonly TimeZoneInfo MadridTz = SpotPower.Core.TimeZones.MadridTimeZone.Instance;

    private readonly ILogger<MarginalPdbcParser> _logger;

    public MarginalPdbcParser(ILogger<MarginalPdbcParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a raw marginalpdbc file into DayAheadPrice rows for both zones.
    ///
    /// ASSUMPTION (not yet verified against OMIE's authoritative file-format
    /// spec at omie.es/en/publicaciones/64): line format is
    ///   Year;Month;Day;Period;PriceSpain;PricePortugal;
    /// with one header line and a trailing "*" terminator line. If the real
    /// file differs, this will throw or silently misparse a column — verify
    /// against a real downloaded file before relying on this in production.
    /// </summary>
    public List<DayAheadPrice> Parse(string rawContent, string sourceFileName, DateOnly expectedDeliveryDate)
    {
        var result = new List<DayAheadPrice>();
        var lines = rawContent.Split('\n', StringSplitOptions.TrimEntries);

        var durationMinutes = expectedDeliveryDate >= FifteenMinuteEraStart ? 15 : 60;

        // Tracks local wall-clock start times already emitted today, so that on
        // the autumn DST fallback day (the 02:00-03:00 local hour occurs twice)
        // we can tell the first occurrence from the second and pick the correct
        // UTC offset for each, instead of guessing.
        var seenLocalStarts = new HashSet<DateTime>();

        // Skip header (line 0); stop at the "*" terminator or end of file.
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('*'))
            {
                break;
            }

            var fields = line.Split(';');
            if (fields.Length < 6)
            {
                _logger.LogWarning(
                    "Skipping malformed line {LineNumber} in {FileName}: {Line}",
                    i, sourceFileName, line);
                continue;
            }

            if (!int.TryParse(fields[0], out var year) ||
                !int.TryParse(fields[1], out var month) ||
                !int.TryParse(fields[2], out var day) ||
                !int.TryParse(fields[3], out var periodNumber))
            {
                _logger.LogWarning(
                    "Skipping line {LineNumber} in {FileName} with unparseable date/period fields: {Line}",
                    i, sourceFileName, line);
                continue;
            }

            var deliveryDate = new DateOnly(year, month, day);
            var periodStartUtc = GetPeriodStartUtc(deliveryDate, periodNumber, durationMinutes, seenLocalStarts);

            if (TryParsePrice(fields[4], out var spainPrice))
            {
                result.Add(BuildRow(deliveryDate, periodNumber, durationMinutes,
                    periodStartUtc, MarketZone.Spain, spainPrice, sourceFileName));
            }
            else
            {
                _logger.LogWarning(
                    "Could not parse Spain price on line {LineNumber} in {FileName}: '{RawValue}'",
                    i, sourceFileName, fields[4]);
            }

            if (TryParsePrice(fields[5], out var portugalPrice))
            {
                result.Add(BuildRow(deliveryDate, periodNumber, durationMinutes,
                    periodStartUtc, MarketZone.Portugal, portugalPrice, sourceFileName));
            }
            else
            {
                _logger.LogWarning(
                    "Could not parse Portugal price on line {LineNumber} in {FileName}: '{RawValue}'",
                    i, sourceFileName, fields[5]);
            }
        }

        _logger.LogInformation(
            "Parsed {RowCount} price row(s) from {FileName} for delivery date {DeliveryDate}.",
            result.Count, sourceFileName, expectedDeliveryDate);

        return result;
    }

    private static DayAheadPrice BuildRow(
        DateOnly deliveryDate, int periodNumber, int durationMinutes,
        DateTime periodStartUtc, MarketZone zone, decimal price, string sourceFileName) => new()
    {
        DeliveryDate = deliveryDate,
        PeriodNumber = periodNumber,
        DurationMinutes = durationMinutes,
        PeriodStartUtc = periodStartUtc,
        Zone = zone,
        PriceEurPerMWh = price,
        SourceFileName = sourceFileName
    };

    private static bool TryParsePrice(string raw, out decimal price)
    {
        // OMIE historically used a comma as decimal separator in some file
        // families; normalize defensively so a stray comma doesn't fail parsing.
        var normalized = raw.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out price);
    }

    private static DateTime GetPeriodStartUtc(
        DateOnly deliveryDate, int periodNumber, int durationMinutes, HashSet<DateTime> seenLocalStarts)
    {
        var localMidnight = deliveryDate.ToDateTime(TimeOnly.MinValue); // Kind = Unspecified
        var localStart = localMidnight.AddMinutes((periodNumber - 1) * durationMinutes);

        if (MadridTz.IsInvalidTime(localStart))
        {
            // Spring-forward gap (clocks jump 02:00 -> 03:00). OMIE's own period
            // numbering already omits this hour on the short day, so this branch
            // should not normally trigger; treat defensively rather than throw.
            localStart = localStart.AddMinutes(durationMinutes);
        }

        if (MadridTz.IsAmbiguousTime(localStart))
        {
            var offsets = MadridTz.GetAmbiguousTimeOffsets(localStart);
            // offsets[0] is the larger (summer/CEST) offset, offsets[1] the
            // smaller (winter/CET) one. First time we see this exact local
            // wall-clock value today -> it's the pre-fallback (CEST) occurrence.
            // Second time -> it's the post-fallback (CET) occurrence.
            var isSecondOccurrence = !seenLocalStarts.Add(localStart);
            var offset = isSecondOccurrence
                ? offsets.Min()
                : offsets.Max();

            return new DateTimeOffset(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), offset)
                .UtcDateTime;
        }

        seenLocalStarts.Add(localStart);
        return TimeZoneInfo.ConvertTimeToUtc(localStart, MadridTz);
    }
}