using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OptimAdhan.Core.Location;

public interface IGeocoder
{
    Task<IReadOnlyList<LocationInfo>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Best place name for coordinates, or null when the service has no answer.</summary>
    Task<LocationInfo?> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

/// <summary>
/// OpenStreetMap Nominatim, the same free service the web app uses. Follows the
/// usage policy (https://operations.osmfoundation.org/policies/nominatim/):
/// identifying User-Agent, at most one request per second, results cached, and
/// searches only on explicit user action (no autocomplete).
/// Only the place name comes from the network; the time zone is resolved offline.
/// </summary>
public sealed class NominatimGeocoder : IGeocoder, IDisposable
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<LocationInfo>> _searchCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public NominatimGeocoder(string userAgent, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName + ",en;q=0.8");
    }

    public async Task<IReadOnlyList<LocationInfo>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        lock (_searchCache)
        {
            if (_searchCache.TryGetValue(query, out var cached))
            {
                return cached;
            }
        }

        var url = $"search?format=jsonv2&addressdetails=1&limit=8&q={Uri.EscapeDataString(query)}";
        var places = await GetAsync<List<NominatimPlace>>(url, cancellationToken) ?? [];

        var results = places
            .Select(ToLocation)
            .OfType<LocationInfo>()
            .DistinctBy(l => l.DisplayName)
            .ToList();

        lock (_searchCache)
        {
            _searchCache[query] = results;
        }

        return results;
    }

    public async Task<LocationInfo?> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"reverse?format=jsonv2&zoom=10&addressdetails=1&lat={latitude:0.######}&lon={longitude:0.######}");
        var place = await GetAsync<NominatimPlace>(url, cancellationToken);
        if (place?.Address is null)
        {
            return null;
        }

        // Keep the precise coordinates the user's device reported, not the place centroid.
        return LocationInfo.Create(FormatName(place.Address, place.Name), latitude, longitude,
            place.Address.CountryCode, LocationSource.Detected);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastRequestUtc + MinInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            _lastRequestUtc = DateTime.UtcNow;
            using var response = await _http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static LocationInfo? ToLocation(NominatimPlace place)
    {
        if (!double.TryParse(place.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(place.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return null;
        }

        var name = place.Address is null ? place.DisplayName ?? place.Name : FormatName(place.Address, place.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return LocationInfo.Create(name, lat, lon, place.Address?.CountryCode, LocationSource.Search);
    }

    /// <summary>"City, State, Country", skipping empty or repeated parts.</summary>
    internal static string FormatName(NominatimAddress address, string? fallbackName)
    {
        var locality = address.City ?? address.Town ?? address.Village ?? address.Municipality
            ?? address.Suburb ?? address.County ?? fallbackName;

        var parts = new[] { locality, address.State, address.Country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var name = string.Join(", ", parts);
        return name.Length > 0 ? name : fallbackName ?? "Unnamed place";
    }

    internal sealed class NominatimPlace
    {
        [JsonPropertyName("lat")] public string? Lat { get; set; }

        [JsonPropertyName("lon")] public string? Lon { get; set; }

        [JsonPropertyName("name")] public string? Name { get; set; }

        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }

        [JsonPropertyName("address")] public NominatimAddress? Address { get; set; }
    }

    internal sealed class NominatimAddress
    {
        [JsonPropertyName("city")] public string? City { get; set; }

        [JsonPropertyName("town")] public string? Town { get; set; }

        [JsonPropertyName("village")] public string? Village { get; set; }

        [JsonPropertyName("municipality")] public string? Municipality { get; set; }

        [JsonPropertyName("suburb")] public string? Suburb { get; set; }

        [JsonPropertyName("county")] public string? County { get; set; }

        [JsonPropertyName("state")] public string? State { get; set; }

        [JsonPropertyName("country")] public string? Country { get; set; }

        [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
    }
}
