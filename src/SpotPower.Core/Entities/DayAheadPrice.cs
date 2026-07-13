namespace SpotPower.Core.Entities;

/// <summary>
/// A single Day-Ahead auction clearing price for one market time unit (MTU)
/// in one bidding zone, as published by OMIE.
///
/// One row = one period. Pre-2025-10-01 deliveries have 23-25 hourly periods/day
/// (DST-dependent). From 2025-10-01 onward (SDAC 15-minute MTU reform),
/// deliveries have up to ~92-100 quarter-hourly periods/day. This entity does not
/// assume either granularity: PeriodStartUtc + DurationMinutes fully describe
/// the period regardless of era.
/// </summary>
public class DayAheadPrice
{
    public long Id { get; set; }

    /// <summary>
    /// Delivery date this period belongs to (the date the auction result is FOR,
    /// not the date the auction was run — auctions run at 12:00 CET the day before).
    /// </summary>
    public DateOnly DeliveryDate { get; set; }

    /// <summary>
    /// 1-based index of this period within the delivery day, as published in the
    /// source file. Kept for traceability back to the raw OMIE row; NOT used as
    /// the uniqueness key because its meaning (hour vs quarter-hour) depends on era.
    /// </summary>
    public int PeriodNumber { get; set; }

    /// <summary>
    /// Duration of this period in minutes: 60 for pre-reform hourly data,
    /// 15 for post-reform quarter-hourly data.
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Exact UTC instant this period starts. This is the canonical identity
    /// field (unambiguous across DST transitions, unlike a bare "hour 3" index).
    /// CET/CEST conversion is a presentation concern, done at the API layer.
    /// </summary>
    public DateTime PeriodStartUtc { get; set; }

    public MarketZone Zone { get; set; }

    /// <summary>
    /// Marginal clearing price in EUR/MWh. Decimal, not double, since this is
    /// a financial value that must round-trip exactly.
    /// </summary>
    public decimal PriceEurPerMWh { get; set; }

    /// <summary>
    /// Source file name this row was imported from (e.g. "marginalpdbc_20260710.1").
    /// Enables traceability and supports duplicate-import detection alongside
    /// the unique index on (PeriodStartUtc, Zone).
    /// </summary>
    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>
    /// When this row was written to the database (audit trail, not business data).
    /// </summary>
    public DateTime ImportedAtUtc { get; set; }
}