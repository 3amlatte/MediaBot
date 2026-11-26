using System.Net.Http;
using System.Text.Json;

namespace MediaRenamer.Services;

public record SearchResult(int Id, string Title, string? Year, string Kind, string? PosterPath = null)
{
    public override string ToString() => Year is null ? Title : $"{Title} ({Year})";
}

public record EpisodeInfo(string Name, string Overview, string AirDate);

public record MovieDetails(int Id, string Title, string Overview, string Year, int? Runtime, double? Rating, string? ImdbId, string? PosterPath);

public record SeasonSummary(int Number, string Name, int EpisodeCount, string AirDate);

public record TvDetails(int Id, string Name, string Overview, string Year, double? Rating, string? PosterPath,
    List<SeasonSummary> Seasons);

public class TmdbClient
{
    const string BaseUrl = "https://api.themoviedb.org/3";
    const string ImageBase = "https://image.tmdb.org/t/p/original";
    static readonly HttpClient Http = new();
    readonly string _apiKey;

    public TmdbClient(string apiKey) => _apiKey = apiKey;

    public static string ImageUrl(string path, string size = "original") =>
        $"https://image.tmdb.org/t/p/{size}{path}";

    async Task<JsonDocument> GetAsync(string path, params (string Key, string Value)[] query)
    {
        var qs = string.Concat(query.Select(q => $"&{q.Key}={Uri.EscapeDataString(q.Value)}"));
        var url = $"{BaseUrl}/{path}?api_key={Uri.EscapeDataString(_apiKey)}{qs}";
        using var resp = await Http.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"TMDB error {(int)resp.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    public record SmartSearch(List<SearchResult> Results, string Query);

    /// <summary>
    /// Searches TV shows, retrying with a cleaned-up title and then progressively
    /// shorter titles, so scene file names and release groups still find a match.
    /// </summary>
    public async Task<SmartSearch> SearchTvSmartAsync(string query)
    {
        foreach (var candidate in Candidates(query))
        {
            var results = await SearchTvAsync(candidate);
            if (results.Count > 0) return new SmartSearch(results, candidate);
        }
        return new SmartSearch(new List<SearchResult>(), query);
    }

    public async Task<SmartSearch> SearchMoviesSmartAsync(string query, int? year)
    {
        foreach (var candidate in Candidates(query))
        {
            var withYear = await SearchMoviesAsync(candidate, year);
            if (withYear.Count > 0) return new SmartSearch(withYear, candidate);
        }

        if (year is not null)
        {
            foreach (var candidate in Candidates(query))
            {
                var anyYear = await SearchMoviesAsync(candidate, null);
                if (anyYear.Count > 0) return new SmartSearch(anyYear, candidate);
            }
        }

        return new SmartSearch(new List<SearchResult>(), query);
    }

    static IEnumerable<string> Candidates(string query)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var original = query.Trim();
        if (seen.Add(original)) yield return original;

        var parsed = FilenameParser.Parse(original).Title;
        if (parsed.Length > 0 && seen.Add(parsed)) yield return parsed;

        var baseTitle = parsed.Length > 0 ? parsed : original;
        var words = baseTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int n = words.Length - 1; n >= 2; n--)
        {
            var candidate = string.Join(" ", words.Take(n));
            if (seen.Add(candidate)) yield return candidate;
        }
    }

    public async Task<List<SearchResult>> SearchMoviesAsync(string query, int? year)
    {
        var q = new List<(string Key, string Value)> { ("query", query) };
        if (year is int y) q.Add(("year", y.ToString()));
        using var doc = await GetAsync("search/movie", q.ToArray());
        return ParseResults(doc, "title", "release_date", "movie");
    }

    public async Task<List<SearchResult>> SearchTvAsync(string query)
    {
        using var doc = await GetAsync("search/tv", ("query", query));
        return ParseResults(doc, "name", "first_air_date", "tv");
    }

    public async Task<Dictionary<int, EpisodeInfo>> GetSeasonEpisodesAsync(int tvId, int season)
    {
        using var doc = await GetAsync($"tv/{tvId}/season/{season}");
        var episodes = new Dictionary<int, EpisodeInfo>();
        if (doc.RootElement.TryGetProperty("episodes", out var eps))
        {
            foreach (var e in eps.EnumerateArray())
            {
                episodes[e.GetProperty("episode_number").GetInt32()] =
                    new EpisodeInfo(Str(e, "name"), Str(e, "overview"), Str(e, "air_date"));
            }
        }
        return episodes;
    }

    public async Task<MovieDetails> GetMovieDetailsAsync(int id)
    {
        using var doc = await GetAsync($"movie/{id}");
        var r = doc.RootElement;
        return new MovieDetails(id, Str(r, "title"), Str(r, "overview"), YearOf(Str(r, "release_date")),
            IntOrNull(r, "runtime"), DoubleOrNull(r, "vote_average"), StrOrNull(r, "imdb_id"), StrOrNull(r, "poster_path"));
    }

    public async Task<TvDetails> GetTvDetailsAsync(int id)
    {
        using var doc = await GetAsync($"tv/{id}");
        var r = doc.RootElement;

        var seasons = new List<SeasonSummary>();
        if (r.TryGetProperty("seasons", out var list))
        {
            foreach (var s in list.EnumerateArray())
            {
                if (IntOrNull(s, "season_number") is not int number) continue;
                seasons.Add(new SeasonSummary(
                    number,
                    Str(s, "name"),
                    IntOrNull(s, "episode_count") ?? 0,
                    Str(s, "air_date")));
            }
        }

        return new TvDetails(id, Str(r, "name"), Str(r, "overview"), YearOf(Str(r, "first_air_date")),
            DoubleOrNull(r, "vote_average"), StrOrNull(r, "poster_path"), seasons);
    }

    static List<SearchResult> ParseResults(JsonDocument doc, string titleProp, string dateProp, string kind)
    {
        var list = new List<SearchResult>();
        if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
        foreach (var r in results.EnumerateArray())
        {
            var title = r.GetProperty(titleProp).GetString() ?? "";
            var date = r.TryGetProperty(dateProp, out var d) ? d.GetString() : null;
            var year = string.IsNullOrEmpty(date) || date.Length < 4 ? null : date[..4];
            list.Add(new SearchResult(r.GetProperty("id").GetInt32(), title, year, kind, StrOrNull(r, "poster_path")));
        }
        return list;
    }

    static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string? StrOrNull(JsonElement e, string prop)
    {
        var s = Str(e, prop);
        return s.Length == 0 ? null : s;
    }

    static int? IntOrNull(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    static double? DoubleOrNull(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static string YearOf(string date) => date.Length >= 4 ? date[..4] : "";
}
