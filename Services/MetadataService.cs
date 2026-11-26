using MediaRenamer.Services;

namespace MediaRenamer.Services;

/// <summary>
/// Routes metadata requests to TMDB or TheTVDB depending on the chosen source.
/// SearchResult.Source records where each result came from, so existing matches
/// keep working even if the preferred source is changed later.
/// </summary>
public class MetadataService
{
    public record SmartSearch(List<SearchResult> Results, string Query);

    readonly AppSettings _settings;
    TmdbClient? _tmdb;
    TvdbClient? _tvdb;

    public MetadataService(AppSettings settings)
    {
        _settings = settings;
        Source = settings.MetadataSource.Equals("tvdb", StringComparison.OrdinalIgnoreCase) ? "tvdb" : "tmdb";
    }

    public string Source { get; }

    public string SourceName => Source == "tvdb" ? "TheTVDB" : "TMDB";

    public bool IsConfigured => Source == "tvdb"
        ? !string.IsNullOrWhiteSpace(_settings.TvdbApiKey)
        : !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public string ConfigurationHint => Source == "tvdb"
        ? "Set your TheTVDB API key in Preferences first."
        : "Set your TMDB API key in Preferences first.";

    TmdbClient Tmdb => _tmdb ??= new TmdbClient(_settings.ApiKey);
    TvdbClient Tvdb => _tvdb ??= new TvdbClient(_settings.TvdbApiKey, _settings.TvdbPin);

    public async Task<SmartSearch> SearchTvAsync(string query)
    {
        foreach (var candidate in Candidates(query))
        {
            var results = Source == "tvdb"
                ? await Tvdb.SearchSeriesAsync(candidate)
                : await Tmdb.SearchTvAsync(candidate);
            if (results.Count > 0) return new SmartSearch(results, candidate);
        }
        return new SmartSearch(new List<SearchResult>(), query);
    }

    public async Task<SmartSearch> SearchMoviesAsync(string query, int? year)
    {
        foreach (var candidate in Candidates(query))
        {
            var results = Source == "tvdb"
                ? await Tvdb.SearchMoviesAsync(candidate, year)
                : await Tmdb.SearchMoviesAsync(candidate, year);
            if (results.Count > 0) return new SmartSearch(results, candidate);
        }

        if (year is not null)
        {
            foreach (var candidate in Candidates(query))
            {
                var results = Source == "tvdb"
                    ? await Tvdb.SearchMoviesAsync(candidate, null)
                    : await Tmdb.SearchMoviesAsync(candidate, null);
                if (results.Count > 0) return new SmartSearch(results, candidate);
            }
        }

        return new SmartSearch(new List<SearchResult>(), query);
    }

    public Task<Dictionary<int, EpisodeInfo>> GetEpisodesAsync(SearchResult show, int season) =>
        show.Source == "tvdb"
            ? Tvdb.GetSeasonEpisodesAsync(show.Id, season)
            : Tmdb.GetSeasonEpisodesAsync(show.Id, season);

    public Task<TvDetails> GetTvDetailsAsync(SearchResult show) =>
        show.Source == "tvdb"
            ? Tvdb.GetSeriesAsync(show.Id)
            : Tmdb.GetTvDetailsAsync(show.Id);

    public Task<MovieDetails> GetMovieDetailsAsync(SearchResult movie) =>
        movie.Source == "tvdb"
            ? Tvdb.GetMovieAsync(movie.Id)
            : Tmdb.GetMovieDetailsAsync(movie.Id);

    /// <summary>TMDB needs a size for its image path; TVDB stores a full URL.</summary>
    public static string PosterUrl(SearchResult result, string tmdbSize)
    {
        if (result.PosterPath is null) return "";
        return result.PosterPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? result.PosterPath
            : TmdbClient.ImageUrl(result.PosterPath, tmdbSize);
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
}
