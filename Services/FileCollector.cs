using MediaRenamer.Models;

namespace MediaRenamer.Services;

public record CollectResult(List<RenameItem> Items, int Skipped);

public static class FileCollector
{
    public const string Filter = "Media files|*.mkv;*.mp4;*.avi;*.m4v;*.mov;*.wmv;*.ts;*.webm;*.srt;*.sub;*.ass;*.ssa;*.vtt|All files|*.*";

    public static CollectResult Collect(IEnumerable<string> paths)
    {
        var items = new List<RenameItem>();
        int skipped = 0;

        foreach (var p in paths)
        {
            var files = Directory.Exists(p)
                ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                : File.Exists(p) ? new[] { p } : Array.Empty<string>();

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!Renamer.VideoExts.Contains(ext) && !Renamer.SubtitleExts.Contains(ext))
                {
                    skipped++;
                    continue;
                }
                items.Add(new RenameItem { SourcePath = file, Parsed = FilenameParser.Parse(file) });
            }
        }

        return new CollectResult(items, skipped);
    }
}
