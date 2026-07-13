namespace SpotPower.Infrastructure.Configuration;

public class OmieOptions
{
    public const string SectionName = "Omie";

    public string BaseUrl { get; set; } = "https://www.omie.es";

    /// <summary>How many days to look back on the very first run (empty database)
    /// to seed some history instead of starting with only "tomorrow".</summary>
    public int InitialBackfillDays { get; set; } = 7;

    /// <summary>Quartz cron expression for the recurring import.</summary>
    public string CronSchedule { get; set; } = "0 5 13 * * ?";
}