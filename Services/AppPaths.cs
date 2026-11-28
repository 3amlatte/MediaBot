namespace MediaRenamer.Services;

/// <summary>
/// Resolves where MediaBot keeps its user data. A portable build ships with a
/// <see cref="PortableMarker"/> file next to MediaBot.exe; when it is present,
/// settings and undo history live in that same folder instead of %AppData%.
/// </summary>
public static class AppPaths
{
    public const string PortableMarker = "MediaBot.portable";

    public static bool IsPortable { get; } =
        File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    public static string DataFolder => IsPortable
        ? AppContext.BaseDirectory
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaRenamer");

    public static string DataFile(string name) => Path.Combine(DataFolder, name);
}
