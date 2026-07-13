using SpotPower.Core.Entities;

namespace SpotPower.Core.Repositories;

/// <summary>
/// Abstraction over persistence for Day-Ahead prices. Implemented against EF Core
/// in Infrastructure; kept here so the Quartz job and any future consumer depend
/// on this interface, not on EF Core directly.
/// </summary>
public interface IDayAheadPriceRepository
{
    /// <summary>
    /// Returns the most recent delivery date already persisted for the given zone,
    /// or null if the store is empty. Used by the import job to decide where to
    /// resume after a restart.
    /// </summary>
    Task<DateOnly?> GetLatestDeliveryDateAsync(MarketZone zone, CancellationToken ct = default);

    /// <summary>
    /// Returns true if any row already exists for this exact period + zone.
    /// </summary>
    Task<bool> ExistsAsync(DateTime periodStartUtc, MarketZone zone, CancellationToken ct = default);

    /// <summary>
    /// Inserts rows that don't already exist (by PeriodStartUtc + Zone) and skips
    /// the rest. Returns the number of rows actually inserted.
    /// </summary>
    Task<int> AddMissingAsync(IEnumerable<DayAheadPrice> prices, CancellationToken ct = default);

    /// <summary>
    /// Retrieves prices for a delivery date range, ordered by PeriodStartUtc.
    /// This is what the REST API's query layer will call.
    /// </summary>
    Task<IReadOnlyList<DayAheadPrice>> GetByDateRangeAsync(
        DateOnly fromDate, DateOnly toDate, MarketZone zone, CancellationToken ct = default);
}