using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotPower.Infrastructure.Configuration;

namespace SpotPower.Infrastructure.ExternalServices;

public class OmieDayAheadClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OmieDayAheadClient> _logger;

    public OmieDayAheadClient(HttpClient http, IOptions<OmieOptions> options, ILogger<OmieDayAheadClient> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl);
        _logger = logger;
    }

    /// <summary>
    /// Downloads the raw marginalpdbc file for the given delivery date.
    /// Returns null if the file isn't published yet (e.g. a future date, or
    /// today before the 12:00 CET auction has produced results) — this is a
    /// normal, expected condition, not an error.
    /// </summary>
    public async Task<string?> DownloadMarginalPdbcAsync(DateOnly deliveryDate, CancellationToken ct = default)
    {
        var fileName = $"marginalpdbc_{deliveryDate:yyyyMMdd}.1";
        var requestUri = $"/en/file-download?parents[0]=marginalpdbc&filename={fileName}";

        _logger.LogDebug("Requesting OMIE file {FileName}", fileName);

        using var response = await _http.GetAsync(requestUri, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "OMIE file {FileName} not available yet (HTTP {StatusCode}).",
                fileName, (int)response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("OMIE file {FileName} returned an empty body.", fileName);
            return null;
        }

        return content;
    }
}