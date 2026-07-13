namespace SpotPower.Web.Models;

/// <summary>
/// Mirrors SpotPower.Api's DayAheadPricePointDto. Intentionally duplicated
/// rather than shared via a project reference: Web has zero reference to
/// any backend project, by design, so it is structurally incapable of
/// bypassing the REST API to touch the database directly.
/// </summary>
public record DayAheadPricePointDto(
    DateTimeOffset PeriodStartCet,
    int DurationMinutes,
    string Zone,
    decimal PriceEurPerMwh);