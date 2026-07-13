namespace SpotPower.Api.Contracts;

/// <summary>
/// One Day-Ahead price point as exposed by the API. PeriodStartCet carries an
/// explicit UTC offset (+01:00 or +02:00 depending on CET/CEST), so consumers
/// never have to guess which one applies on a given date.
/// </summary>
public record DayAheadPricePointDto(
    DateTimeOffset PeriodStartCet,
    int DurationMinutes,
    string Zone,
    decimal PriceEurPerMwh);