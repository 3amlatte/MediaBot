using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using MediaRenamer.Models;

namespace MediaRenamer.Services;

public static class MetadataWriter
{
    static readonly HttpClient Http = new();

    public static string MovieNfo(MovieDetails m) => Document(new XElement("movie",
        new XElement("title", m.Title),
        new XElement("year", m.Year),
        new XElement("plot", m.Overview),
        new XElement("runtime", m.Runtime?.ToString(CultureInfo.InvariantCulture) ?? ""),
        new XElement("rating", m.Rating?.ToString("0.0", CultureInfo.InvariantCulture) ?? ""),
        new XElement("uniqueid", new XAttribute("type", "tmdb"), new XAttribute("default", "true"), m.Id),
        m.ImdbId is null ? null : new XElement("uniqueid", new XAttribute("type", "imdb"), m.ImdbId)));

    public static string TvShowNfo(TvDetails t) => Document(new XElement("tvshow",
        new XElement("title", t.Name),
        new XElement("year", t.Year),
        new XElement("plot", t.Overview),
        new XElement("rating", t.Rating?.ToString("0.0", CultureInfo.InvariantCulture) ?? ""),
        new XElement("uniqueid", new XAttribute("type", "tmdb"), new XAttribute("default", "true"), t.Id)));

    public static string EpisodeNfo(string showTitle, int season, int episode, EpisodeInfo e) => Document(new XElement("episodedetails",
        new XElement("title", e.Name),
        new XElement("showtitle", showTitle),
        new XElement("season", season),
        new XElement("episode", episode),
        new XElement("plot", e.Overview),
        new XElement("aired", e.AirDate)));

    public static async Task<List<string>> WriteAsync(IEnumerable<RenameItem> items)
    {
        var created = new List<string>();
        var handledFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.Where(i => i.Status == "Renamed"))
        {
            var dir = Path.GetDirectoryName(item.SourcePath)!;
            var stem = Path.GetFileNameWithoutExtension(item.SourcePath);

            if (item.Nfo is not null)
                WriteIfMissing(Path.Combine(dir, stem + ".nfo"), item.Nfo, created);

            if (item.FolderDir is null || !handledFolders.Add(item.FolderDir)) continue;
            Directory.CreateDirectory(item.FolderDir);

            if (item.FolderNfo is not null)
                WriteIfMissing(Path.Combine(item.FolderDir, "tvshow.nfo"), item.FolderNfo, created);

            if (item.FolderPosterUrl is not null)
            {
                var poster = Path.Combine(item.FolderDir, "poster.jpg");
                if (!File.Exists(poster))
                {
                    try
                    {
                        var bytes = await Http.GetByteArrayAsync(item.FolderPosterUrl);
                        await File.WriteAllBytesAsync(poster, bytes);
                        created.Add(poster);
                    }
                    catch (HttpRequestException)
                    {
                    }
                }
            }
        }

        return created;
    }

    static void WriteIfMissing(string path, string content, List<string> created)
    {
        if (File.Exists(path)) return;
        File.WriteAllText(path, content);
        created.Add(path);
    }

    static string Document(XElement root) =>
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString();
}
