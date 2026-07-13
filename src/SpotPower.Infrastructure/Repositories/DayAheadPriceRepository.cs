using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpotPower.Core.Entities;
using SpotPower.Core.Repositories;
using SpotPower.Infrastructure.Persistence;

namespace SpotPower.Infrastructure.Repositories;

public class DayAheadPriceRepository : IDayAheadPriceRepository
{
    private readonly SpotPowerDbContext _db;
    private readonly ILogger<DayAheadPriceRepository> _logger;

    public DayAheadPriceRepository(SpotPowerDbContext db, ILogger<DayAheadPriceRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DateOnly?> GetLatestDeliveryDateAsync(MarketZone zone, CancellationToken ct = default)
    {
        // Ordering by PeriodStartUtc (the real instant), not DeliveryDate, so "latest"
        // is unambiguous even near a UTC/CET calendar-day boundary.
        var latest = await _db.DayAheadPrices
            .Where(p => p.Zone == zone)
            .OrderByDescending(p => p.PeriodStartUtc)
            .Select(p => p.DeliveryDate)
            .FirstOrDefaultAsync(ct);

        return latest == default ? null : latest;
    }

    public async Task<bool> ExistsAsync(DateTime periodStartUtc, MarketZone zone, CancellationToken ct = default)
    {
        // Exact instant match only. Deliberately NOT filtering by DeliveryDate or
        // any "start of day / end of day" range here: Spain is UTC+1/+2 (CET/CEST),
        // so periods early in the Spanish delivery day land on the PREVIOUS UTC
        // calendar date. Bucketing by day caused real duplicate-insert bugs before.
        // PeriodStartUtc is a precise instant, and instants compare correctly
        // regardless of which calendar day they happen to fall in, in any timezone.
        return await _db.DayAheadPrices
            .AnyAsync(p => p.PeriodStartUtc == periodStartUtc && p.Zone == zone, ct);
    }

    public async Task<int> AddMissingAsync(IEnumerable<DayAheadPrice> prices, CancellationToken ct = default)
    {
        var candidates = prices.ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Single query: fetch the set of (PeriodStartUtc, Zone) pairs that ALREADY
        // exist among the candidates' zones, then filter in memory by the exact
        // instant + zone pair. This avoids N round-trips (one ExistsAsync per row)
        // while still comparing exact timestamps, never calendar days.
        var zones = candidates.Select(c => c.Zone).Distinct().ToList();
        var candidateInstants = candidates.Select(c => c.PeriodStartUtc).ToList();

        var existingKeys = await _db.DayAheadPrices
            .Where(p => zones.Contains(p.Zone) && candidateInstants.Contains(p.PeriodStartUtc))
            .Select(p => new { p.PeriodStartUtc, p.Zone })
            .ToListAsync(ct);

        var existingSet = existingKeys
            .Select(k => (k.PeriodStartUtc, k.Zone))
            .ToHashSet();

        var toInsert = candidates
            .Where(c => !existingSet.Contains((c.PeriodStartUtc, c.Zone)))
            .ToList();

        var skipped = candidates.Count - toInsert.Count;
        if (skipped > 0)
        {
            _logger.LogInformation(
                "Skipped {SkippedCount} already-imported period(s) out of {TotalCount} candidates.",
                skipped, candidates.Count);
        }

        if (toInsert.Count == 0)
        {
            return 0;
        }

        foreach (var price in toInsert)
        {
            price.ImportedAtUtc = DateTime.UtcNow;
        }

        _db.DayAheadPrices.AddRange(toInsert);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Backstop: the unique DB index on (PeriodStartUtc, Zone) is the final
            // guard if a race or a bug in the in-memory check above ever lets a
            // duplicate slip through. Log it clearly rather than crashing the job.
            _logger.LogError(ex,
                "Insert failed, likely a duplicate slipped past the in-memory check " +
                "and hit the unique index on (PeriodStartUtc, Zone). No rows were committed " +
                "from this batch of {Count}.", toInsert.Count);
            throw;
        }

        _logger.LogInformation("Inserted {InsertedCount} new price period(s).", toInsert.Count);
        return toInsert.Count;
    }

    public async Task<IReadOnlyList<DayAheadPrice>> GetByDateRangeAsync(
        DateOnly fromDate, DateOnly toDate, MarketZone zone, CancellationToken ct = default)
    {
        // This one legitimately filters by DeliveryDate, which is fine here: this
        // method serves the REST API's "give me delivery date X to Y" queries,
        // where DeliveryDate is the actual business concept being asked about,
        // not a duplicate-detection shortcut. The distinction that matters is
        // WHY you're filtering by date, not whether it's ever okay to do so.
        return await _db.DayAheadPrices
            .Where(p => p.Zone == zone && p.DeliveryDate >= fromDate && p.DeliveryDate <= toDate)
            .OrderBy(p => p.PeriodStartUtc)
            .ToListAsync(ct);
    }
}