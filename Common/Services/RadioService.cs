using KyappuTone.Core.Radio;

namespace KyappuTone.Common.Services;

/// <summary>Station search and saved favorites backed by the Radio Browser API.</summary>
public static class RadioService
{
    private const string BaseUrl = "https://de1.api.radio-browser.info/json/stations/search";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<IReadOnlyList<RadioStation>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var parameters = "limit=50&hidebroken=true&order=clickcount&reverse=true";
        if (!string.IsNullOrWhiteSpace(query))
            parameters += $"&name={Uri.EscapeDataString(query.Trim())}";

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}?{parameters}");
        request.Headers.UserAgent.ParseAdd("KyappuTone/0.3.0 (https://github.com/YoshKoz/KyappuTone)");
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Radio Browser returned {(int)response.StatusCode}.");

        return RadioBrowserMapper.ParseStations(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public static async Task<List<RadioStation>> GetFavoritesAsync()
    {
        var rows = await DatabaseHelper.Instance.GetRadioFavorites();
        return rows.Select(row => new RadioStation(
                row.Uuid,
                row.Name,
                row.Url,
                row.FaviconUrl,
                row.Tags,
                row.Country,
                row.Codec,
                row.Bitrate))
            .ToList();
    }

    public static async Task<bool> ToggleFavoriteAsync(RadioStation station)
    {
        if (await DatabaseHelper.Instance.IsRadioFavorite(station.Uuid))
        {
            await DatabaseHelper.Instance.RemoveRadioFavorite(station.Uuid);
            return false;
        }

        await DatabaseHelper.Instance.UpsertRadioFavorite(station);
        return true;
    }

    public static Task TouchFavoriteAsync(string uuid)
        => DatabaseHelper.Instance.TouchRadioFavorite(uuid);
}
