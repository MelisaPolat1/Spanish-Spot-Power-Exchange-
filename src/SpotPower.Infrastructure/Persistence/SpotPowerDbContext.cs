using Microsoft.EntityFrameworkCore;
using SpotPower.Core.Entities;

namespace SpotPower.Infrastructure.Persistence;

public class SpotPowerDbContext : DbContext
{
    public SpotPowerDbContext(DbContextOptions<SpotPowerDbContext> options) : base(options)
    {
    }

    public DbSet<DayAheadPrice> DayAheadPrices => Set<DayAheadPrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DayAheadPrice>(entity =>
        {
            entity.ToTable("DayAheadPrices");
            entity.HasKey(e => e.Id);

            // This is the duplicate-import guard: the database itself will reject
            // a second row for the same period + zone, so even if the Quartz job's
            // own existence-check has a bug or races with a manual re-run, we
            // cannot end up with duplicate data.
            entity.HasIndex(e => new { e.PeriodStartUtc, e.Zone })
                  .IsUnique();

            // Delivery date is queried constantly by the API (date-range lookups),
            // so it gets its own index too.
            entity.HasIndex(e => e.DeliveryDate);

            entity.Property(e => e.PriceEurPerMWh)
                  .HasPrecision(10, 2); // EUR/MWh: plenty of headroom, 2 decimal places

            entity.Property(e => e.SourceFileName)
                  .HasMaxLength(100);

            entity.Property(e => e.Zone)
                  .HasConversion<string>() // store enum as readable text, not magic int
                  .HasMaxLength(20);
        });
    }
}