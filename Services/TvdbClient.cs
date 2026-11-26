using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MediaRenamer.Services;

/// <summary>
/// Read-only client for TheTVDB API v4 (https://thetvdb.github.io/v4-api/).
/// Requires a free TVDB API key; a PIN is only needed for subscriber keys.
/// </summary>
public class TvdbClient
{
    const string BaseUrl = "https://api4.thetvdb.com/v4";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly string _apiKey;
    readonly string _pin;
    string? _token;

    public TvdbClient(string apiKey, string pin = "")
    {
        _apiKey = apiKey.Trim();
        _pin = pin.Trim();
    }

    public async Task<List<SearchResult>> SearchSeriesAsync(string query)
    {
        using var doc = await GetAsync("search", ("query", query), ("type", "series"));
        return ParseSearch(doc, "series", "tv");
    }

    public async Task<List<SearchResult>> SearchMoviesAsync(string query, int? year)
    {
        using var doc = await GetAsync("search", ("query", query), ("type", "movie"));
        var results = ParseSearch(doc, "movie", "movie");
        if (year is int y)
        {
            var filtered = results.Where(r => r.Year == y.ToString()).ToList();
            if (filtered.Count > 0) return filtered;
        }
        return results;
    }

    public async Task<TvDetails> GetSeriesAsync(int id)
    {
        using var doc = await GetAsync($"series/{id}/extended");
        var d = doc.RootElement.GetProperty("data");

        var seasons = new List<SeasonSummary>();
        if (d.TryGetProperty("seasons", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in list.EnumerateArray())
            {
                if (IntOrNull(s, "number") is not int number) continue;
                seasons.Add(new SeasonSummary(number, Str(s, "name"), 0, Str(s, "name")));
            }
        }

        var year = Str(d, "year");
        if (year.Length == 0 && Str(d, "firstAired") is { Length: >= 4 } aired) year = aired[..4];

        return new TvDetails(
            id,
            Str(d, "name"),
            Str(d, "overview"),
            year,
            DoubleOrNull(d, "score"),
            StrOrNull(d, "image"),
            seasons);
    }

    public async Task<MovieDetails> GetMovieAsync(int id)
    {
        using var doc = await GetAsync($"movies/{id}/extended");
        var d = doc.RootElement.GetProperty("data");

        var year = Str(d, "year");
        if (year.Length == 0 && Str(d, "first_release") is { Length: >= 4 } released) year = released[..4];

        string? imdb = null;
        if (d.TryGetProperty("remoteIds", out var remote) && remote.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in remote.EnumerateArray())
            {
                var source = Str(r, "sourceName");
                if (source.Equals("IMDB", StringComparison.OrdinalIgnoreCase))
                {
                    imdb = StrOrNull(r, "id");
                    break;
                }
            }
        }

        return new MovieDetails(
            id,
            Str(d, "name"),
            Str(d, "overview"),
            year,
            IntOrNull(d, "runtime"),
            DoubleOrNull(d, "score"),
            imdb,
            StrOrNull(d, "image"));
    }

    public async Task<Dictionary<int, EpisodeInfo>> GetSeasonEpisodesAsync(int seriesId, int season)
    {
        var episodes = new Dictionary<int, EpisodeInfo>();

        for (int page = 0; page < 50; page++)
        {
            using var doc = await GetAsync($"series/{seriesId}/episodes/default",
                ("page", page.ToString()), ("season", season.ToString()));

            if (!doc.RootElement.TryGetProperty("data", out var data)) break;

            if (data.TryGetProperty("episodes", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in list.EnumerateArray())
                {
                    if (IntOrNull(e, "seasonNumber") != season) continue;
                    int number = IntOrNull(e, "number") ?? 0;
                    if (number <= 0) continue;
                    episodes[number] = new EpisodeInfo(Str(e, "name"), Str(e, "overview"), Str(e, "aired"));
                }
            }

            var next = data.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var n)
                ? n.GetString()
                : null;
            if (string.IsNullOrEmpty(next)) break;
        }

        return episodes;
    }

    static List<SearchResult> ParseSearch(JsonDocument doc, string type, string kind)
    {
        var list = new List<SearchResult>();
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in data.EnumerateArray())
        {
            var itemType = Str(item, "type");
            if (!itemType.Equals(type, StringComparison.OrdinalIgnoreCase)) continue;

            var idText = Str(item, "tvdb_id");
            if (idText.Length == 0)
            {
                var objectId = Str(item, "objectID");
                var dash = objectId.LastIndexOf('-');
                if (dash >= 0) idText = objectId[(dash + 1)..];
            }
            if (!int.TryParse(idText, out var id)) continue;

            var year = Str(item, "year");
            list.Add(new SearchResult(
                id,
                Str(item, "name"),
                year.Length > 0 ? year : null,
                kind,
                StrOrNull(item, "image_url"),
                "tvdb"));
        }
        return list;
    }

    async Task EnsureTokenAsync()
    {
        if (_token is not null) return;

        var payload = new Dictionary<string, string> { ["apikey"] = _apiKey };
        if (_pin.Length > 0) payload["pin"] = _pin;

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync($"{BaseUrl}/login", content);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(resp.StatusCode == HttpStatusCode.Unauthorized
                ? "TVDB rejected the API key. Check the key (and PIN if you set one) in Preferences."
                : $"TVDB login failed ({(int)resp.StatusCode}).");

        using var doc = JsonDocument.Parse(body);
        _token = doc.RootElement.GetProperty("data").GetProperty("token").GetString();
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("TVDB did not return a login token.");
    }

    async Task<JsonDocument> GetAsync(string path, params (string Key, string Value)[] query)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await EnsureTokenAsync();

            var url = $"{BaseUrl}/{path}";
            if (query.Length > 0)
                url += "?" + string.Join("&", query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.UserAgent.ParseAdd("MediaBot/2.0");

            HttpResponseMessage resp;
            try
            {
                resp = await Http.SendAsync(req);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException("Could not reach TheTVDB: " + ex.Message);
            }

            using (resp)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _token = null;
                    continue;
                }

                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"TheTVDB returned HTTP {(int)resp.StatusCode} for {path}.");
                return JsonDocument.Parse(body);
            }
        }

        throw new InvalidOperationException("TheTVDB authentication failed.");
    }

    static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.ToString(),
            _ => ""
        };
    }

    static string? StrOrNull(JsonElement e, string name)
    {
        var s = Str(e, name);
        return s.Length == 0 ? null : s;
    }

    static int? IntOrNull(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    static double? DoubleOrNull(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
}
