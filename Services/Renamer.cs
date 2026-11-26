using System.Text;
using System.Text.RegularExpressions;
using MediaRenamer.Models;

namespace MediaRenamer.Services;

public record NamingOptions(string TvTemplate, string MovieTemplate, string TvFolderTemplate, string MovieFolderTemplate, string? OutputRoot);

public record RenameMove(string From, string To);

public record RenameBatch(DateTime Time, List<RenameMove> Moves, List<string> CreatedDirs, List<string>? CreatedFiles = null);

public static class Renamer
{
    public static readonly string[] VideoExts = { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".webm" };
    public static readonly string[] SubtitleExts = { ".srt", ".sub", ".ass", ".ssa", ".vtt" };

    public static void Build(IList<RenameItem> items, SearchResult match,
        Dictionary<int, Dictionary<int, EpisodeInfo>> episodes, NamingOptions options)
    {
        bool tv = match.Kind == "tv";
        var videos = items.Where(i => !i.IsSubtitle).ToList();

        foreach (var item in items)
        {
            item.Nfo = null;
            item.FolderDir = null;
            item.FolderNfo = null;
            item.FolderPosterUrl = null;
        }

        foreach (var item in videos)
        {
            var sourceDir = Path.GetDirectoryName(item.SourcePath)!;
            if (tv)
            {
                var p = item.Parsed;
                if (p.Season is not int s || p.Episode is not int ep)
                {
                    item.NewName = item.OriginalName;
                    item.TargetDir = sourceDir;
                    item.Status = "No SxxExx tag";
                    continue;
                }
                string? epTitle = episodes.TryGetValue(s, out var eps) && eps.TryGetValue(ep, out var info) ? info.Name : null;
                item.NewName = Format(options.TvTemplate, match.Title, s, ep, epTitle, null) + item.Extension;
                item.TargetDir = TargetDirectory(sourceDir, options.OutputRoot, options.TvFolderTemplate, match.Title, s, null);
                item.FolderDir = options.OutputRoot is null ? null : ShowDirectory(options.OutputRoot, options.TvFolderTemplate, match.Title);
            }
            else
            {
                var year = match.Year ?? item.Parsed.Year?.ToString();
                item.NewName = Format(options.MovieTemplate, match.Title, null, null, null, year) + item.Extension;
                item.TargetDir = TargetDirectory(sourceDir, options.OutputRoot, options.MovieFolderTemplate, match.Title, null, year);
                item.FolderDir = options.OutputRoot is null ? null : item.TargetDir;
            }
            item.Status = "Ready";
        }

        foreach (var sub in items.Where(i => i.IsSubtitle))
        {
            sub.TargetDir = Path.GetDirectoryName(sub.SourcePath)!;
            var video = FindVideo(sub, videos);
            if (video is null)
            {
                sub.NewName = sub.OriginalName;
                sub.Status = "No matching video";
                continue;
            }
            var suffix = SubSuffix(sub, video);
            sub.NewName = Path.GetFileNameWithoutExtension(video.NewName) + suffix + sub.Extension;
            sub.TargetDir = video.TargetDir;
            sub.Status = "Ready";
        }
    }

    public static RenameBatch Apply(IEnumerable<RenameItem> items)
    {
        var moves = new List<RenameMove>();
        var created = new List<string>();

        foreach (var item in items.Where(i => i.Status == "Ready"))
        {
            var target = Path.Combine(item.TargetDir, item.NewName);
            if (target == item.SourcePath)
            {
                item.Status = "Unchanged";
                continue;
            }

            bool caseOnly = string.Equals(target, item.SourcePath, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && File.Exists(target))
            {
                item.Status = "Skipped: file exists";
                continue;
            }

            try
            {
                created.AddRange(EnsureDirectory(item.TargetDir));
                File.Move(item.SourcePath, target);
                moves.Add(new RenameMove(item.SourcePath, target));
                item.SourcePath = target;
                item.Parsed = FilenameParser.Parse(target);
                item.Status = "Renamed";
            }
            catch (IOException ex)
            {
                item.Status = "Error: " + ex.Message;
            }
        }

        return new RenameBatch(DateTime.Now, moves, created, new());
    }

    public static List<RenameMove> Undo(RenameBatch batch)
    {
        foreach (var file in batch.CreatedFiles ?? new())
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
        batch.CreatedFiles?.Clear();

        var restored = new List<RenameMove>();

        foreach (var move in batch.Moves.AsEnumerable().Reverse().ToList())
        {
            if (!File.Exists(move.To) || File.Exists(move.From)) continue;
            try
            {
                File.Move(move.To, move.From);
                restored.Add(move);
                batch.Moves.Remove(move);
            }
            catch (IOException)
            {
            }
        }

        foreach (var dir in batch.CreatedDirs)
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (IOException)
            {
            }
        }

        return restored;
    }

    static List<string> EnsureDirectory(string dir)
    {
        var created = new List<string>();
        for (var d = dir; !string.IsNullOrEmpty(d) && !Directory.Exists(d); d = Path.GetDirectoryName(d))
            created.Add(d);
        Directory.CreateDirectory(dir);
        return created;
    }

    static string TargetDirectory(string sourceDir, string? root, string folderTemplate, string title, int? season, string? year)
    {
        if (root is null) return sourceDir;

        var dir = root;
        foreach (var segment in folderTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = Format(segment, title, season, null, null, year);
            if (part.Length > 0) dir = Path.Combine(dir, part);
        }
        return dir;
    }

    static string ShowDirectory(string root, string folderTemplate, string title)
    {
        var dir = root;
        foreach (var segment in folderTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries).TakeWhile(s => !s.Contains("{s}")))
        {
            var part = Format(segment, title, null, null, null, null);
            if (part.Length > 0) dir = Path.Combine(dir, part);
        }
        return dir;
    }

    static RenameItem? FindVideo(RenameItem sub, List<RenameItem> videos)
    {
        var ready = videos.Where(v => v.Status == "Ready").ToList();
        if (sub.Parsed.Season is int s && sub.Parsed.Episode is int e)
            return ready.FirstOrDefault(v => v.Parsed.Season == s && v.Parsed.Episode == e);

        return ready
            .Where(v => sub.Stem.StartsWith(v.Stem, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Stem.Length)
            .FirstOrDefault();
    }

    static string SubSuffix(RenameItem sub, RenameItem video)
    {
        if (sub.Parsed.TagEnd >= 0 && video.Parsed.Season is not null)
            return sub.Stem[sub.Parsed.TagEnd..];
        return sub.Stem[video.Stem.Length..];
    }

    static string Format(string template, string title, int? season, int? episode, string? epTitle, string? year)
    {
        var s = template
            .Replace("{n}", title)
            .Replace("{s}", season?.ToString("00") ?? "")
            .Replace("{e}", episode?.ToString("00") ?? "")
            .Replace("{t}", epTitle ?? "")
            .Replace("{y}", year ?? "");
        s = Regex.Replace(s, @"\s*\(\s*\)", "");
        s = Regex.Replace(s, @"[\s\-]+$", "");
        return Sanitize(s).Trim();
    }

    static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (c == ':') sb.Append('-');
            else if (!invalid.Contains(c)) sb.Append(c);
        }
        return sb.ToString();
    }
}
