using System.Globalization;
using System.Text;
using SpotPower.Api.Contracts;
using SpotPower.Core.Entities;
using SpotPower.Core.Repositories;
using SpotPower.Core.TimeZones;

namespace SpotPower.Api.Endpoints;

public static class DayAheadPriceEndpoints
{
    public static void MapDayAheadPriceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/day-ahead-prices", GetDayAheadPrices)
           .WithName("GetDayAheadPrices");
    }

    private static async Task<IResult> GetDayAheadPrices(
        HttpContext httpContext,
        IDayAheadPriceRepository repository,
        DateOnly? fromDate,
        DateOnly? toDate,
        string zone = "Spain",
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<MarketZone>(zone, ignoreCase: true, out var marketZone))
        {
            return Results.BadRequest(
                $"Invalid zone '{zone}'. Valid values: {string.Join(", ", Enum.GetNames<MarketZone>())}");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = fromDate ?? today;
        var to = toDate ?? from;

        if (to < from)
        {
            return Results.BadRequest("toDate must not be earlier than fromDate.");
        }

        var entities = await repository.GetByDateRangeAsync(from, to, marketZone, ct);
        var points = entities.Select(ToDto).OrderBy(p => p.PeriodStartCet).ToList();

        // Simple manual content negotiation: text/plain gets a semicolon CSV,
        // everything else (including the default) gets JSON. Not using ASP.NET
        // Core's full IResult formatter pipeline here since the CSV shape is a
        // custom, non-standard format the built-in formatters don't produce.
        var acceptHeader = httpContext.Request.Headers.Accept.ToString();
        var wantsPlainText = acceptHeader.Contains("text/plain", StringComparison.OrdinalIgnoreCase);

        return wantsPlainText
            ? Results.Text(BuildCsv(points), "text/plain")
            : Results.Ok(points);
    }

    private static DayAheadPricePointDto ToDto(DayAheadPrice entity)
    {
        var utcInstant = DateTime.SpecifyKind(entity.PeriodStartUtc, DateTimeKind.Utc);
        var offset = MadridTimeZone.Instance.GetUtcOffset(utcInstant);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcInstant, MadridTimeZone.Instance);

        return new DayAheadPricePointDto(
            PeriodStartCet: new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), offset),
            DurationMinutes: entity.DurationMinutes,
            Zone: entity.Zone.ToString(),
            PriceEurPerMwh: entity.PriceEurPerMWh);
    }

    private static string BuildCsv(IReadOnlyList<DayAheadPricePointDto> points)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PeriodStartCet;DurationMinutes;Zone;PriceEurPerMwh");

        foreach (var p in points)
        {
            sb.AppendLine(string.Join(';',
                p.PeriodStartCet.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                p.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                p.Zone,
                p.PriceEurPerMwh.ToString("F2", CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }
}