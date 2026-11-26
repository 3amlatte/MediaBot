using System.Text.RegularExpressions;
using MediaRenamer.Models;

namespace MediaRenamer.Services;

public static class GapAnalyzer
{
    static readonly Regex DriveRx = new(@"^([A-Za-z]):[\\/]", RegexOptions.Compiled);
    static readonly Regex SplitRx = new(@"[\\/]+", RegexOptions.Compiled);

    public static GapReport Analyze(IList<SonarrSeries> series, IDictionary<int, List<SonarrEpisode>> episodes,
        IList<QueueItem> queue, bool includeSpecials, bool monitoredOnly, DateTime nowUtc)
    {
        var queueEpisodes = new HashSet<int>();
        var queuePacks = new HashSet<(int SeriesId, int Season)>();
        foreach (var item in queue)
        {
            if (item.EpisodeId is int eid && eid > 0) queueEpisodes.Add(eid);
            else if (item.SeriesId is int sid && item.Season is int season) queuePacks.Add((sid, season));
        }

        var report = new GapReport();
        var storage = new Dictionary<string, long>();

        foreach (var s in series)
        {
            report.Scanned++;
            bool anime = IsAnime(s);
            if (anime) report.AnimeScanned++;
            report.TotalEpisodes += s.TotalEpisodes;
            report.EpisodesOnDisk += s.EpisodeFiles;

            var root = DriveRoot(s.Path);
            storage[root] = storage.GetValueOrDefault(root) + s.SizeOnDisk;

            if (!s.Monitored) report.UnmonitoredSeries++;
            if (monitoredOnly && !s.Monitored) continue;

            var seasons = new Dictionary<int, SeasonGap>();
            if (episodes.TryGetValue(s.Id, out var eps))
            {
                foreach (var ep in eps)
                {
                    if (ep.Season == 0 && !includeSpecials) continue;
                    if (!seasons.TryGetValue(ep.Season, out var gap))
                    {
                        gap = new SeasonGap { Season = ep.Season };
                        seasons[ep.Season] = gap;
                    }

                    if (s.Monitored && !ep.Monitored) continue;
                    if (ep.AirUtc is not DateTime aired || aired > nowUtc) continue;

                    gap.MonitoredAired++;
                    if (ep.HasFile) continue;

                    if (queueEpisodes.Contains(ep.Id) || queuePacks.Contains((s.Id, ep.Season)))
                    {
                        gap.Downloading++;
                        continue;
                    }

                    gap.Missing.Add(new MissingEpisode(ep.Id, ep.Season, ep.Number, ep.Absolute, ep.Title, ep.AirDate));
                }
            }

            var gaps = seasons.Values
                .Where(g => g.Missing.Count > 0 || g.Downloading > 0)
                .OrderBy(g => g.Season)
                .ToList();
            if (gaps.Count == 0) continue;

            var seriesGap = new SeriesGap
            {
                SeriesId = s.Id,
                Title = s.Title,
                Year = s.Year,
                SeriesType = anime ? "anime" : s.SeriesType,
                Network = s.Network,
                Status = s.Status,
                Monitored = s.Monitored
            };
            seriesGap.Seasons.AddRange(gaps);
            report.Series.Add(seriesGap);
            report.DownloadingTotal += seriesGap.Downloading;
        }

        report.Series = report.Series
            .OrderByDescending(x => x.MissingCount)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        report.Drives = storage
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key == "Unknown")
            .ThenBy(kv => kv.Key)
            .Select(kv => new DriveUsage { Root = kv.Key, LibraryBytes = kv.Value })
            .ToList();

        return report;
    }

    public static void AttachDriveSpace(GapReport report)
    {
        foreach (var drive in report.Drives)
        {
            if (drive.Root.Length != 2 || drive.Root[1] != ':') continue;
            try
            {
                var info = new DriveInfo(drive.Root + "\\");
                if (!info.IsReady) continue;
                drive.TotalBytes = info.TotalSize;
                drive.FreeBytes = info.AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = Math.Max(bytes, 0);
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        if (index == 0) return $"{(long)value} B";
        int decimals = value < 10 ? 2 : 1;
        return $"{value.ToString("N" + decimals)} {units[index]}";
    }

    static bool IsAnime(SonarrSeries s)
    {
        if (s.SeriesType.Equals("anime", StringComparison.OrdinalIgnoreCase)) return true;

        var root = s.RootFolder;
        if (root.Length == 0)
        {
            var parts = SplitRx.Split(s.Path);
            root = parts.Length > 1 ? parts[1] : "";
        }
        return root.Contains("anime", StringComparison.OrdinalIgnoreCase);
    }

    static string DriveRoot(string path)
    {
        var m = DriveRx.Match(path);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant() + ":";
        if (path.Length > 0)
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root)) return root;
        }
        return "Unknown";
    }
}
