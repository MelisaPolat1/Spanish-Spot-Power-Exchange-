namespace SpotPower.Core.Entities;

/// <summary>
/// The MIBEL bidding zone a price applies to.
/// OMIE publishes separate marginal prices for Spain and Portugal
/// (identical when interconnection capacity is sufficient; they split
/// under congestion / market splitting).
/// </summary>
public enum MarketZone
{
    Spain = 1,
    Portugal = 2
}
