using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using MediaRenamer.Models;

namespace MediaRenamer.Services;

public record SonarrSeries(int Id, string Title, int? Year, string SeriesType, string Network, string Status,
    bool Monitored, string Path, string RootFolder, int EpisodeFiles, int TotalEpisodes, long SizeOnDisk);

public record SonarrEpisode(int Id, int Season, int Number, int? Absolute, string Title, DateTime? AirUtc,
    string? AirDate, bool Monitored, bool HasFile);

public record QueueItem(int? SeriesId, int? EpisodeId, int? Season);

public class SonarrClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly string _apiKey;

    public string BaseUrl { get; }

    public SonarrClient(string url, string apiKey)
    {
        var u = url.Trim().TrimEnd('/');
        if (u.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase)) u = u[..^"/api/v3".Length];
        if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Sonarr URL must start with http:// or https://.");
        BaseUrl = u;
        _apiKey = apiKey.Trim();
    }

    public async Task<string> VersionAsync()
    {
        using var doc = await GetAsync("/api/v3/system/status");
        return Str(doc.RootElement, "version", "unknown");
    }

    public async Task<List<SonarrSeries>> SeriesAsync()
    {
        using var doc = await GetAsync("/api/v3/series");
        var list = new List<SonarrSeries>();
        foreach (var s in doc.RootElement.EnumerateArray())
        {
            var parsed = ParseSeries(s);
            if (parsed is not null) list.Add(parsed);
        }
        return list;
    }

    public async Task<List<SonarrEpisode>> EpisodesAsync(int seriesId)
    {
        using var doc = await GetAsync("/api/v3/episode", ("seriesId", seriesId.ToString()));
        var list = new List<SonarrEpisode>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var season = IntOrNull(e, "seasonNumber");
            var number = IntOrNull(e, "episodeNumber");
            var id = IntOrNull(e, "id");
            if (season is not int s || number is not int n) continue;

            list.Add(new SonarrEpisode(
                id ?? 0,
                s,
                n,
                IntOrNull(e, "absoluteEpisodeNumber"),
                Str(e, "title", $"Episode {n}"),
                ParseAir(e),
                Str(e, "airDate") is { Length: > 0 } d ? d : null,
                BoolOr(e, "monitored", true),
                BoolOr(e, "hasFile", false)));
        }
        return list;
    }

    public async Task<(Dictionary<int, List<SonarrEpisode>> Episodes, List<string> Errors)> EpisodesForAsync(
        IEnumerable<int> seriesIds, Action<int, int>? progress = null)
    {
        var ids = seriesIds.ToList();
        var episodes = new Dictionary<int, List<SonarrEpisode>>();
        var errors = new List<string>();
        using var gate = new SemaphoreSlim(6);
        int done = 0;

        var tasks = ids.Select(async id =>
        {
            await gate.WaitAsync();
            try
            {
                episodes[id] = await EpisodesAsync(id);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"Series {id}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
            progress?.Invoke(Interlocked.Increment(ref done), ids.Count);
        }).ToList();

        await Task.WhenAll(tasks);
        return (episodes, errors);
    }

    public async Task<List<QueueItem>> QueueAsync()
    {
        var items = new List<QueueItem>();
        for (int page = 1; page <= 100; page++)
        {
            using var doc = await GetAsync("/api/v3/queue",
                ("page", page.ToString()),
                ("pageSize", "250"),
                ("includeUnknownSeriesItems", "true"));
            var root = doc.RootElement;
            if (!root.TryGetProperty("records", out var records)) break;

            int count = 0;
            foreach (var r in records.EnumerateArray())
            {
                count++;
                items.Add(new QueueItem(IntOrNull(r, "seriesId"), IntOrNull(r, "episodeId"), IntOrNull(r, "seasonNumber")));
            }

            var total = IntOrNull(root, "totalRecords");
            if (count == 0 || (total is int t && items.Count >= t)) break;
        }
        return items;
    }

    async Task<JsonDocument> GetAsync(string path, params (string Key, string Value)[] query)
    {
        var url = BaseUrl + path;
        if (query.Length > 0)
            url += "?" + string.Join("&", query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Api-Key", _apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.UserAgent.ParseAdd("MediaBot/2.0");

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException($"Could not reach Sonarr at {BaseUrl}: {ex.Message}");
        }

        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Sonarr rejected the API key (401). Check Settings > General > API Key.");
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Sonarr returned 404 for {path}. Is this a Sonarr v3 or v4 URL?");
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Sonarr returned HTTP {(int)resp.StatusCode} for {path}.");
            return JsonDocument.Parse(body);
        }
    }

    static SonarrSeries? ParseSeries(JsonElement s)
    {
        var id = IntOrNull(s, "id");
        if (id is not int seriesId) return null;

        var stats = s.TryGetProperty("statistics", out var st) ? st : s;
        var type = Str(s, "seriesType");
        var total = IntOrNull(stats, "totalEpisodeCount") ?? IntOrNull(stats, "episodeCount") ?? 0;

        return new SonarrSeries(
            seriesId,
            Str(s, "title", $"Series {seriesId}"),
            IntOrNull(s, "year"),
            type.Length > 0 ? type : "standard",
            Str(s, "network"),
            Str(s, "status"),
            BoolOr(s, "monitored", true),
            Str(s, "path"),
            Str(s, "rootFolderPath"),
            IntOrNull(stats, "episodeFileCount") ?? 0,
            total,
            LongOf(stats, "sizeOnDisk"));
    }

    static DateTime? ParseAir(JsonElement e)
    {
        var utc = Str(e, "airDateUtc");
        if (utc.Length > 0 && DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;

        var date = Str(e, "airDate");
        if (date.Length > 0 && DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;

        return null;
    }

    static string Str(JsonElement e, string name, string fallback = "") =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    static int? IntOrNull(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    static long LongOf(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;

    static bool BoolOr(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : fallback;
}
