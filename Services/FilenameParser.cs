using System.Text.RegularExpressions;

namespace MediaRenamer.Services;

public record ParsedName(string Title, int? Season, int? Episode, int? Year, int TagEnd);

public static class FilenameParser
{
    static readonly Regex SxxExx = new(@"[Ss](\d{1,2})[ ._-]?[Ee](\d{1,3})", RegexOptions.Compiled);
    static readonly Regex NxN = new(@"(?<!\d)(\d{1,2})x(\d{2,3})(?!\d)", RegexOptions.Compiled);
    static readonly Regex YearRx = new(@"[\(\[\s._-](19\d{2}|20\d{2})(?=[\)\]\s._-]|$)", RegexOptions.Compiled);
    static readonly Regex BracketRx = new(@"\[[^\]]*\]|\([^)]*\)", RegexOptions.Compiled);
    static readonly Regex JunkRx = new(
        @"\b(1080p|720p|2160p|480p|4k|uhd|bluray|blu-ray|brrip|bdrip|dvdrip|dvdscr|hdrip|web-?dl|webrip|hdtv|x264|x265|h\.?264|h\.?265|hevc|av1|10bit|8bit|aac|ac3|eac3|dts|ddp\d?|dd5\.?1|atmos|truehd|remux|proper|repack|amzn|dsnp|hmax|atvp)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedName Parse(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        int? season = null, episode = null, year = null;
        int cut = name.Length;
        int tagEnd = -1;

        var m = SxxExx.Match(name);
        if (!m.Success) m = NxN.Match(name);
        if (m.Success)
        {
            season = int.Parse(m.Groups[1].Value);
            episode = int.Parse(m.Groups[2].Value);
            cut = m.Index;
            tagEnd = m.Index + m.Length;
        }

        var ym = YearRx.Matches(name);
        if (ym.Count > 0)
        {
            var last = ym[^1];
            year = int.Parse(last.Groups[1].Value);
            cut = Math.Min(cut, last.Index);
        }

        var title = name[..cut];
        title = JunkRx.Replace(title, "");
        title = BracketRx.Replace(title, " ");
        title = StripReleaseGroup(title);
        title = title.Replace('.', ' ').Replace('_', ' ');
        title = Regex.Replace(title, @"\s+", " ").Trim(' ', '-');

        return new ParsedName(title, season, episode, year, tagEnd);
    }

    /// <summary>
    /// Drops a trailing scene release group, e.g. "...x264-SPARKS" or "Show.Name-LOL",
    /// without touching normal hyphenated titles such as "Spider-Man".
    /// </summary>
    static string StripReleaseGroup(string title)
    {
        int idx = title.LastIndexOf('-');
        if (idx <= 0) return title;

        var tail = title[(idx + 1)..].Trim();
        if (tail.Length is < 2 or > 20) return title;
        if (!tail.All(c => char.IsLetterOrDigit(c) || c is '@' or '.')) return title;

        int upper = tail.Count(char.IsUpper);
        var letters = tail.Where(char.IsLetter).ToList();
        bool allCaps = letters.Count > 0 && letters.All(char.IsUpper);
        if (upper < 2 && !allCaps) return title;

        var head = title[..idx].Trim();
        return head.Length >= 2 ? head : title;
    }
}
