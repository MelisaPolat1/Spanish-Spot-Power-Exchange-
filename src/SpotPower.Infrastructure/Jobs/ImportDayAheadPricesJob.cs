using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using SpotPower.Core.Entities;
using SpotPower.Core.Repositories;
using SpotPower.Infrastructure.Configuration;
using SpotPower.Infrastructure.ExternalServices;
using SpotPower.Infrastructure.Parsing;

namespace SpotPower.Infrastructure.Jobs;

/// <summary>
/// Periodically downloads and imports OMIE Day-Ahead results. Designed to be
/// safely re-run at any time (on schedule, or after a restart): it resumes
/// from the day after the latest delivery date already stored, and relies on
/// AddMissingAsync's exact-instant duplicate check, so re-processing an
/// already-imported day is a harmless no-op rather than an error.
/// </summary>
public class ImportDayAheadPricesJob : IJob
{
    private readonly OmieDayAheadClient _client;
    private readonly MarginalPdbcParser _parser;
    private readonly IDayAheadPriceRepository _repository;
    private readonly OmieOptions _options;
    private readonly ILogger<ImportDayAheadPricesJob> _logger;

    public ImportDayAheadPricesJob(
        OmieDayAheadClient client,
        MarginalPdbcParser parser,
        IDayAheadPriceRepository repository,
        IOptions<OmieOptions> options,
        ILogger<ImportDayAheadPricesJob> logger)
    {
        _client = client;
        _parser = parser;
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        _logger.LogInformation("Day-Ahead import job started at {StartedAtUtc}.", DateTime.UtcNow);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tomorrow = today.AddDays(1); // latest delivery date that could possibly be published

        var latest = await _repository.GetLatestDeliveryDateAsync(MarketZone.Spain, ct);
        var startDate = latest?.AddDays(1) ?? today.AddDays(-_options.InitialBackfillDays);

        if (startDate > tomorrow)
        {
            _logger.LogInformation("Already up to date; nothing to import.");
            return;
        }

        for (var date = startDate; date <= tomorrow; date = date.AddDays(1))
        {
            await ImportSingleDayAsync(date, ct);
        }

        _logger.LogInformation("Day-Ahead import job finished at {FinishedAtUtc}.", DateTime.UtcNow);
    }

    private async Task ImportSingleDayAsync(DateOnly deliveryDate, CancellationToken ct)
    {
        var fileName = $"marginalpdbc_{deliveryDate:yyyyMMdd}.1";

        string? rawContent;
        try
        {
            rawContent = await _client.DownloadMarginalPdbcAsync(deliveryDate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download {FileName}.", fileName);
            return;
        }

        if (rawContent is null)
        {
            // Not published yet (expected for "tomorrow" before the 12:00 CET
            // auction has cleared) — nothing to do, later runs will pick it up.
            return;
        }

        List<DayAheadPrice> prices;
        try
        {
            prices = _parser.Parse(rawContent, fileName, deliveryDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse {FileName}; skipping this file.", fileName);
            return;
        }

        if (prices.Count == 0)
        {
            _logger.LogWarning("{FileName} parsed to zero rows.", fileName);
            return;
        }

        try
        {
            var inserted = await _repository.AddMissingAsync(prices, ct);
            _logger.LogInformation(
                "Imported {FileName}: {Inserted} new row(s) out of {Total} parsed.",
                fileName, inserted, prices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist rows from {FileName}.", fileName);
        }
    }
}