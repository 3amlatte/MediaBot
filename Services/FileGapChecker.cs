using MediaRenamer.Models;

namespace MediaRenamer.Services;

public static class FileGapChecker
{
    public static FileCheckResult Numbering(IEnumerable<RenameItem> files)
    {
        var result = new FileCheckResult();
        var groups = files
            .Where(f => f.Parsed.Season is > 0 && f.Parsed.Episode is not null)
            .GroupBy(f => (Show: NormalizeTitle(f.Parsed.Title), Season: f.Parsed.Season!.Value))
            .OrderBy(g => g.Key.Show)
            .ThenBy(g => g.Key.Season);

        foreach (var group in groups)
        {
            var have = group.Select(f => f.Parsed.Episode!.Value).ToHashSet();
            int low = have.Min();
            int high = have.Max();
            var missing = Enumerable.Range(low, high - low + 1).Where(n => !have.Contains(n)).ToList();
            if (missing.Count > 0)
                result.Numbering.Add(new FileNumberingGap(group.First().Parsed.Title, group.Key.Season, missing));
        }

        return result;
    }

    public static async Task AddSonarrComparisonAsync(FileCheckResult result, IEnumerable<RenameItem> files,
        SonarrClient client, bool includeSpecials, Action<string> progress)
    {
        result.SonarrChecked = true;
        var series = await client.SeriesAsync();
        var nowUtc = DateTime.UtcNow;
        var groups = files
            .Where(f => f.Parsed.Season is not null && f.Parsed.Episode is not null)
            .GroupBy(f => NormalizeTitle(f.Parsed.Title));

        foreach (var group in groups)
        {
            var show = group.First().Parsed.Title;
            var match = series.FirstOrDefault(s => NormalizeTitle(s.Title) == group.Key);
            if (match is null)
            {
                result.NotInSonarr.Add(show);
                continue;
            }

            progress($"Checking {match.Title} against Sonarr...");
            var episodes = await client.EpisodesAsync(match.Id);
            var have = group.Select(f => (f.Parsed.Season!.Value, f.Parsed.Episode!.Value)).ToHashSet();
            var missing = episodes
                .Where(e => (includeSpecials || e.Season > 0) && e.AirUtc is DateTime aired && aired <= nowUtc)
                .Where(e => !have.Contains((e.Season, e.Number)))
                .OrderBy(e => e.Season)
                .ThenBy(e => e.Number)
                .Select(e => new MissingEpisode(e.Id, e.Season, e.Number, e.Absolute, e.Title, e.AirDate))
                .ToList();

            if (missing.Count > 0) result.Sonarr.Add(new FileSonarrGap(match.Title, missing));
        }
    }

    static string NormalizeTitle(string title) =>
        new string(title.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
