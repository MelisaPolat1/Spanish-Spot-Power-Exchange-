namespace SpotPower.Core.TimeZones;

/// <summary>
/// Single source of truth for the MIBEL/Spain timezone (CET/CEST), used both
/// when converting OMIE's local delivery-time periods to UTC on import, and
/// when converting stored UTC instants back to CET for API responses.
/// </summary>
public static class MadridTimeZone
{
    public static readonly TimeZoneInfo Instance = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
}