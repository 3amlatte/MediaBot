using System.IO;
using System.Text.RegularExpressions;
using MediaRenamer.Models;

namespace MediaRenamer.Services;

public static class ListExporter
{
    static readonly string[] Columns = { "original", "new", "path", "folder", "ext", "size", "status", "title", "season", "episode", "year" };

    public static List<string> Text(IEnumerable<RenameItem> items, string template, SearchResult? match) =>
        items.Select(i => Fill(template, Values(i, match))).ToList();

    public static List<string> Csv(IEnumerable<RenameItem> items, SearchResult? match)
    {
        var lines = new List<string> { string.Join(",", Columns.Select(Quote)) };
        foreach (var item in items)
        {
            var values = Values(item, match);
            lines.Add(string.Join(",", Columns.Select(c => Quote(values[c]))));
        }
        return lines;
    }

    static Dictionary<string, string> Values(RenameItem item, SearchResult? match)
    {
        var size = File.Exists(item.SourcePath) ? new FileInfo(item.SourcePath).Length : 0;
        return new Dictionary<string, string>
        {
            ["original"] = item.OriginalName,
            ["new"] = item.NewName,
            ["path"] = item.SourcePath,
            ["folder"] = Path.GetDirectoryName(item.SourcePath) ?? "",
            ["ext"] = item.Extension.TrimStart('.'),
            ["size"] = FormatSize(size),
            ["status"] = item.Status,
            ["title"] = match?.Title ?? item.Parsed.Title,
            ["season"] = item.Parsed.Season?.ToString("00") ?? "",
            ["episode"] = item.Parsed.Episode?.ToString("00") ?? "",
            ["year"] = match?.Year ?? item.Parsed.Year?.ToString() ?? ""
        };
    }

    static string Fill(string template, Dictionary<string, string> values) =>
        Regex.Replace(template, @"\{(\w+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.0} {units[unit]}";
    }
}
