using MediaRenamer.Services;

namespace MediaRenamer.Models;

public class RenameItem
{
    public string SourcePath { get; set; } = "";
    public string OriginalName => Path.GetFileName(SourcePath);
    public string Extension => Path.GetExtension(SourcePath);
    public string Stem => Path.GetFileNameWithoutExtension(SourcePath);
    public bool IsSubtitle => Renamer.SubtitleExts.Contains(Extension.ToLowerInvariant());
    public ParsedName Parsed { get; set; } = null!;
    public string NewName { get; set; } = "";
    public string TargetDir { get; set; } = "";
    public string Status { get; set; } = "Pending";

    public string? Nfo { get; set; }
    public string? FolderDir { get; set; }
    public string? FolderNfo { get; set; }
    public string? FolderPosterUrl { get; set; }

    public string DisplayNew => Status == "Pending" ? ""
        : Status == "Ready" ? NewName
        : $"{NewName}  [{Status}]";
}
