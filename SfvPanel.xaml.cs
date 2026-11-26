using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MediaRenamer.Models;
using MediaRenamer.Services;
using Microsoft.Win32;

namespace MediaRenamer;

public partial class SfvPanel : UserControl
{
    readonly List<string> _paths = new();
    bool _ready;

    public SfvPanel()
    {
        InitializeComponent();
        AlgoCombo.ItemsSource = Checksums.Algorithms;
        AlgoCombo.SelectedIndex = 0;
        _ready = true;
        UpdateMode();
    }

    bool VerifyMode => VerifyRadio.IsChecked == true;

    public void Open(IEnumerable<RenameItem> items)
    {
        AddPaths(items.Select(i => i.SourcePath));
    }

    void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) UpdateMode();
    }

    void UpdateMode()
    {
        RunButton.Content = VerifyMode ? "Verify" : "Create checksums";
        AlgoCombo.IsEnabled = !VerifyMode;
        LookupButton.Visibility = VerifyMode ? Visibility.Visible : Visibility.Collapsed;
        InfoText.Text = VerifyMode
            ? "Verify checks checksum files (.sfv, .md5, .sha1, .sha256) against the files beside them. Files with [CRC32] in their name are checked too. Look up on srrdb identifies files by their CRC32."
            : "Create writes one checksum file per folder, using the algorithm picked above.";
    }

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "All files|*.*" };
        if (dlg.ShowDialog() == true) AddPaths(dlg.FileNames);
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _paths.Clear();
        ResultList.ItemsSource = null;
        RefreshFiles();
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddPaths(paths);
            e.Handled = true;
        }
    }

    void AddPaths(IEnumerable<string> paths)
    {
        var known = _paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            var files = Directory.Exists(p)
                ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                : File.Exists(p) ? new[] { p } : Array.Empty<string>();

            foreach (var file in files)
            {
                if (known.Add(file)) _paths.Add(file);
            }
        }
        RefreshFiles();
    }

    void RefreshFiles()
    {
        FileList.ItemsSource = null;
        FileList.ItemsSource = _paths.Select(Path.GetFileName).ToList();
        InfoText.Text = VerifyMode
            ? $"{_paths.Count} file(s) loaded. Click Verify to check them."
            : $"{_paths.Count} file(s) loaded. Click Create checksums to write checksum files.";
    }

    async void Run_Click(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        try
        {
            if (VerifyMode) await VerifyAsync();
            else await CreateAsync();
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    async Task VerifyAsync()
    {
        var checksumFiles = _paths.Where(p => Checksums.AlgorithmForFile(p) is not null).ToList();
        var dataFiles = _paths.Where(p => Checksums.AlgorithmForFile(p) is null).ToList();
        var rows = new List<string>();
        int ok = 0, bad = 0, missing = 0;

        foreach (var file in checksumFiles)
        {
            var dir = Path.GetDirectoryName(file)!;
            var algo = Checksums.AlgorithmForFile(file)!;
            rows.Add($"-- {Path.GetFileName(file)} --");

            foreach (var entry in Checksums.ParseFile(file))
            {
                var full = Path.Combine(dir, entry.FileName);
                if (!File.Exists(full))
                {
                    rows.Add($"MISSING  {entry.FileName}");
                    missing++;
                    continue;
                }

                var actual = await Task.Run(() => Checksums.Compute(full, algo));
                if (string.Equals(actual, entry.Expected, StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add($"OK       {entry.FileName}");
                    ok++;
                }
                else
                {
                    rows.Add($"BAD      {entry.FileName}");
                    bad++;
                }
            }
        }

        var embedded = dataFiles.Where(f => Checksums.EmbeddedCrc(f) is not null).ToList();
        if (embedded.Count > 0)
        {
            rows.Add("-- Names with [CRC32] --");
            foreach (var file in embedded)
            {
                var expected = Checksums.EmbeddedCrc(file)!;
                var actual = await Task.Run(() => Checksums.Compute(file, "crc32"));
                if (actual == expected)
                {
                    rows.Add($"OK       {Path.GetFileName(file)}");
                    ok++;
                }
                else
                {
                    rows.Add($"BAD      {Path.GetFileName(file)}");
                    bad++;
                }
            }
        }

        if (checksumFiles.Count == 0 && embedded.Count == 0)
            rows.Add("Nothing to verify. Add a .sfv, .md5, .sha1 or .sha256 file, or files with [CRC32] in the name.");

        ResultList.ItemsSource = rows;
        InfoText.Text = $"{ok} OK, {bad} bad, {missing} missing.";
    }

    async void Lookup_Click(object sender, RoutedEventArgs e)
    {
        var files = _paths.Where(p => Checksums.AlgorithmForFile(p) is null).ToList();
        if (files.Count == 0)
        {
            InfoText.Text = "Add media files first, then look them up.";
            return;
        }

        const int limit = 40;
        var targets = files.Take(limit).ToList();
        var rows = new List<string>();
        int matched = 0, found = 0;
        LookupButton.IsEnabled = false;

        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var file = targets[i];
                InfoText.Text = $"Looking up {i + 1} of {targets.Count}: {Path.GetFileName(file)}";
                try
                {
                    var crc = await Task.Run(() => Checksums.Compute(file, "crc32"));
                    var outcome = await LookupFileAsync(file, crc);
                    rows.Add(outcome);
                    if (outcome.StartsWith("MATCH")) matched++;
                    else if (outcome.StartsWith("FOUND")) found++;
                }
                catch (Exception ex)
                {
                    rows.Add($"ERROR    {Path.GetFileName(file)}  —  {ex.Message}");
                }

                ResultList.ItemsSource = null;
                ResultList.ItemsSource = rows.ToList();
            }

            if (files.Count > limit)
                rows.Add($"Stopped after {limit} files. Add fewer files at a time to look up more.");

            ResultList.ItemsSource = rows;
            InfoText.Text = $"{matched} file(s) matched a release, {found} found a release without a matching file, " +
                            $"{targets.Count - matched - found} not found on srrdb.";
        }
        finally
        {
            LookupButton.IsEnabled = true;
        }
    }

    static async Task<string> LookupFileAsync(string file, string crc)
    {
        var client = new SrrdbClient();
        var name = Path.GetFileName(file);
        var stem = Path.GetFileNameWithoutExtension(file);

        // A CRC with no match makes the API ignore the filter and list the whole database,
        // so only a handful of results counts as a real candidate.
        var byCrc = await client.SearchByCrcAsync(crc);
        var candidates = byCrc.Count is > 0 and <= 20
            ? byCrc.Select(r => r.Name)
            : Enumerable.Empty<string>();

        string? nameMatch = null;
        var byName = await client.SearchByNameAsync(stem);
        var exact = byName.FirstOrDefault(r => r.Name.Equals(stem, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) nameMatch = exact.Name;

        var toCheck = candidates.Concat(nameMatch is null ? Array.Empty<string>() : new[] { nameMatch })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        foreach (var releaseName in toCheck)
        {
            var details = await client.DetailsAsync(releaseName);
            var match = details?.Files.FirstOrDefault(f => f.Crc.Equals(crc, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return $"MATCH    {name}  →  {releaseName}  ({match.Name})";
        }

        if (nameMatch is not null)
            return $"FOUND    {name}  →  {nameMatch}  (release found, but no file in it has CRC {crc})";

        return $"NOT FOUND  {name}  (CRC {crc})";
    }

    async Task CreateAsync()
    {
        var algo = (string)AlgoCombo.SelectedItem;
        var dataFiles = _paths.Where(p => Checksums.AlgorithmForFile(p) is null).ToList();
        if (dataFiles.Count == 0)
        {
            InfoText.Text = "Add files first.";
            return;
        }

        var rows = new List<string>();
        int written = 0;

        foreach (var group in dataFiles.GroupBy(p => Path.GetDirectoryName(p)!))
        {
            var lines = new List<string>();
            if (algo == "crc32") lines.Add("; Created by MediaBot");

            foreach (var file in group.OrderBy(Path.GetFileName))
            {
                var hash = await Task.Run(() => Checksums.Compute(file, algo));
                lines.Add(Checksums.FormatLine(algo, Path.GetFileName(file), hash));
                rows.Add($"{hash}  {Path.GetFileName(file)}");
            }

            var folderName = Path.GetFileName(group.Key.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(folderName)) folderName = "checksums";
            var outPath = UniquePath(Path.Combine(group.Key, folderName + Checksums.Extension(algo)));

            File.WriteAllLines(outPath, lines, new UTF8Encoding(false));
            rows.Add($"Wrote {outPath}");
            written++;
        }

        ResultList.ItemsSource = rows;
        InfoText.Text = $"Wrote {written} checksum file(s).";
    }

    static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
