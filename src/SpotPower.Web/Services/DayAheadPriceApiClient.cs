using System.Net.Http.Json;
using System.Text.Json;
using SpotPower.Web.Models;

namespace SpotPower.Web.Services;

public class DayAheadPriceApiClient
{
    private readonly HttpClient _http;

    // JsonSerializerOptions.Web matches ASP.NET Core's own default output
    // conventions (camelCase property names, case-insensitive matching).
    // Without this, "periodStartCet" (from the API's JSON) would silently
    // fail to bind to the "PeriodStartCet" record parameter, since
    // GetFromJsonAsync's plain default options are case-SENSITIVE.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    public DayAheadPriceApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<DayAheadPricePointDto>> GetPricesAsync(
        DateOnly date, string zone, CancellationToken ct = default)
    {
        var url = $"/api/day-ahead-prices?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&zone={Uri.EscapeDataString(zone)}";

        var result = await _http.GetFromJsonAsync<List<DayAheadPricePointDto>>(url, JsonOptions, ct);
        return result ?? [];
    }
}