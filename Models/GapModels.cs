namespace MediaRenamer.Models;

public record MissingEpisode(int EpisodeId, int Season, int Number, int? Absolute, string Title, string? AirDate);

public class SeasonGap
{
    public int Season { get; init; }
    public List<MissingEpisode> Missing { get; } = new();
    public int MonitoredAired { get; set; }
    public int Downloading { get; set; }
    public bool EntireSeason => MonitoredAired > 0 && Missing.Count == MonitoredAired;
}

public class SeriesGap
{
    public int SeriesId { get; init; }
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public string SeriesType { get; init; } = "standard";
    public string Network { get; init; } = "";
    public string Status { get; init; } = "";
    public bool Monitored { get; init; } = true;
    public List<SeasonGap> Seasons { get; } = new();

    public int MissingCount => Seasons.Sum(s => s.Missing.Count);
    public int Downloading => Seasons.Sum(s => s.Downloading);
    public bool IsAnime => SeriesType.Equals("anime", StringComparison.OrdinalIgnoreCase);
}

public class DriveUsage
{
    public string Root { get; init; } = "";
    public long LibraryBytes { get; init; }
    public long? TotalBytes { get; set; }
    public long? FreeBytes { get; set; }
}

public class GapReport
{
    public DateTime Generated { get; init; } = DateTime.Now;
    public string ServerUrl { get; set; } = "";
    public string ServerVersion { get; set; } = "";
    public int Scanned { get; set; }
    public int AnimeScanned { get; set; }
    public int UnmonitoredSeries { get; set; }
    public int TotalEpisodes { get; set; }
    public int EpisodesOnDisk { get; set; }
    public int DownloadingTotal { get; set; }
    public List<DriveUsage> Drives { get; set; } = new();
    public List<SeriesGap> Series { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public int MissingTotal => Series.Sum(s => s.MissingCount);
    public int SeriesWithGaps => Series.Count(s => s.MissingCount > 0);
    public List<(SeriesGap Series, SeasonGap Season)> EntireSeasons =>
        Series.SelectMany(s => s.Seasons.Where(g => g.EntireSeason).Select(g => (s, g))).ToList();
}

public record FileNumberingGap(string Show, int Season, List<int> Missing);

public record FileSonarrGap(string Show, List<MissingEpisode> Missing);

public class FileCheckResult
{
    public List<FileNumberingGap> Numbering { get; } = new();
    public bool SonarrChecked { get; set; }
    public List<FileSonarrGap> Sonarr { get; } = new();
    public List<string> NotInSonarr { get; } = new();
    public List<string> Errors { get; } = new();
}
