using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediaRenamer.Models;
using MediaRenamer.Services;
using Microsoft.Win32;

namespace MediaRenamer;

public partial class MissingPanel : UserControl
{
    readonly List<RenameItem> _files = new();
    AppSettings _settings = new();
    GapReport? _sonarrReport;
    FileCheckResult? _fileResult;
    bool _ready;

    public MissingPanel()
    {
        InitializeComponent();
        _ready = true;
        UpdateModeUi();
        ShowResults();
    }

    bool FilesMode => FilesRadio.IsChecked == true;

    (string Url, string Key)? ResolveSonarr()
    {
        if (!string.IsNullOrWhiteSpace(_settings.SonarrUrl) && !string.IsNullOrWhiteSpace(_settings.SonarrApiKey))
            return (_settings.SonarrUrl, _settings.SonarrApiKey);

        var found = SonarrLocator.Detect();
        return found is null ? null : (found.Url, found.ApiKey);
    }

    public void Open(IEnumerable<RenameItem> items, AppSettings settings)
    {
        _settings = settings;
        AddFiles(items);
    }

    void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        UpdateModeUi();
        ShowResults();
    }

    void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) ShowResults();
    }

    void UpdateModeUi()
    {
        bool files = FilesMode;
        AddButton.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.Content = files ? "Check Files" : "Scan Sonarr";
        InfoText.Text = files
            ? (_files.Count == 0 ? "Add files or drag them into this panel." : $"{_files.Count} video file(s) loaded.")
            : "Scans your Sonarr library for missing episodes.";
    }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (FilesMode) await CheckFilesAsync();
        else await ScanSonarrAsync();
    }

    async Task ScanSonarrAsync()
    {
        var connection = ResolveSonarr();
        if (connection is null)
        {
            InfoText.Text = "Sonarr wasn't found on this PC. Enter the URL and API key in Preferences.";
            return;
        }

        SetBusy(true);
        try
        {
            var client = new SonarrClient(connection.Value.Url, connection.Value.Key);
            InfoText.Text = "Connecting to Sonarr...";
            var version = await client.VersionAsync();
            var series = await client.SeriesAsync();
            var queue = await client.QueueAsync();
            var (episodes, errors) = await client.EpisodesForAsync(
                series.Select(s => s.Id),
                (done, total) => InfoText.Text = $"Fetching episodes {done} of {total}...");

            var report = GapAnalyzer.Analyze(series, episodes, queue,
                _settings.SonarrIncludeSpecials, _settings.SonarrMonitoredOnly, DateTime.UtcNow);
            report.ServerUrl = client.BaseUrl;
            report.ServerVersion = version;
            report.Errors.AddRange(errors);
            GapAnalyzer.AttachDriveSpace(report);

            _sonarrReport = report;
            InfoText.Text = $"Scanned {report.Scanned} series.";
            ShowResults();
        }
        catch (Exception ex)
        {
            InfoText.Text = "Scan failed: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    async Task CheckFilesAsync()
    {
        var videos = _files.Where(f => !f.IsSubtitle).ToList();
        if (videos.Count == 0)
        {
            InfoText.Text = "Add video files first.";
            return;
        }

        var result = FileGapChecker.Numbering(videos);
        var connection = ResolveSonarr();
        if (connection is not null)
        {
            SetBusy(true);
            try
            {
                var client = new SonarrClient(connection.Value.Url, connection.Value.Key);
                await FileGapChecker.AddSonarrComparisonAsync(result, videos, client,
                    _settings.SonarrIncludeSpecials, msg => InfoText.Text = msg);
            }
            catch (Exception ex)
            {
                result.Errors.Add("Sonarr check failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        _fileResult = result;
        InfoText.Text = $"Checked {videos.Count} file(s).";
        ShowResults();
    }

    void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : null;
    }

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = FileCollector.Filter };
        if (dlg.ShowDialog() == true) AddFiles(FileCollector.Collect(dlg.FileNames).Items);
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _files.Clear();
        _fileResult = null;
        UpdateModeUi();
        ShowResults();
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
            AddFiles(FileCollector.Collect(paths).Items);
            e.Handled = true;
        }
    }

    void AddFiles(IEnumerable<RenameItem> items)
    {
        var known = _files.Select(f => f.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var item in items.Where(i => !i.IsSubtitle))
        {
            if (!known.Add(item.SourcePath)) continue;
            _files.Add(item);
            added++;
        }

        if (added > 0) _fileResult = null;
        UpdateModeUi();
        if (added > 0) InfoText.Text = $"Added {added} video file(s). Click Check Files.";
        ShowResults();
    }

    void ShowResults()
    {
        ResultTree.Items.Clear();
        var filter = FilterBox.Text.Trim();
        if (FilesMode) ShowFileResults(filter);
        else ShowSonarrResults(filter);
    }

    void ShowSonarrResults(string filter)
    {
        if (_sonarrReport is null)
        {
            SummaryText.Text = "";
            AddNote("Click Scan Sonarr to check your library.");
            return;
        }

        var r = _sonarrReport;
        var summary = $"{r.SeriesWithGaps} series with gaps  ·  {r.MissingTotal} missing episodes  ·  " +
                      $"{r.EntireSeasons.Count} entirely missing seasons  ·  {r.DownloadingTotal} downloading\n" +
                      $"{r.Scanned} shows, {r.TotalEpisodes:N0} episodes ({r.EpisodesOnDisk:N0} on disk)";
        var drives = DriveText(r);
        SummaryText.Text = drives.Length > 0 ? summary + "\n" + drives : summary;

        bool Match(string title) => filter.Length == 0 || title.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var missing = r.Series.Where(s => s.MissingCount > 0 && Match(s.Title)).ToList();
        var anime = missing.Where(s => s.IsAnime).ToList();
        var tv = missing.Where(s => !s.IsAnime).ToList();
        if (anime.Count > 0) ResultTree.Items.Add(Group($"Anime ({anime.Count})", anime.Select(SeriesNode)));
        if (tv.Count > 0) ResultTree.Items.Add(Group($"TV shows ({tv.Count})", tv.Select(SeriesNode)));

        var entire = r.EntireSeasons.Where(x => Match(x.Series.Title)).ToList();
        if (entire.Count > 0)
        {
            ResultTree.Items.Add(Group($"Entirely missing seasons ({entire.Count})",
                entire.Select(x => Leaf($"{x.Series.Title}  —  Season {x.Season.Season} ({x.Season.Missing.Count} episodes)"))));
        }

        var downloading = r.Series.Where(s => s.MissingCount == 0 && s.Downloading > 0 && Match(s.Title)).ToList();
        if (downloading.Count > 0)
        {
            ResultTree.Items.Add(Group($"Downloading, nothing missing ({downloading.Count})",
                downloading.Select(s => Leaf($"{s.Title}  —  " + string.Join(", ",
                    s.Seasons.Where(g => g.Downloading > 0).Select(g => $"Season {g.Season} ({g.Downloading})"))))));
        }

        if (r.Errors.Count > 0)
        {
            ResultTree.Items.Add(Group($"Warnings ({r.Errors.Count})", r.Errors.Select(Leaf)));
        }

        if (ResultTree.Items.Count == 0)
            AddNote(filter.Length > 0 ? "No series match this filter." : "No missing episodes found.");
    }

    void ShowFileResults(string filter)
    {
        if (_fileResult is null)
        {
            SummaryText.Text = "";
            AddNote(_files.Count == 0
                ? "Add files, or drag a folder into this panel. Only video files are checked."
                : "Click Check Files to compare the loaded files.");
            return;
        }

        var r = _fileResult;
        SummaryText.Text = $"{_files.Count(f => !f.IsSubtitle)} video file(s) checked  ·  " +
                           $"{r.Numbering.Count} gap(s) in numbering  ·  " +
                           (r.SonarrChecked
                               ? $"{r.Sonarr.Count} show(s) missing episodes in Sonarr"
                               : "Sonarr not checked (set it up in Preferences)");

        bool Match(string show) => filter.Length == 0 || show.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var numbering = r.Numbering.Where(g => Match(g.Show)).GroupBy(g => g.Show).ToList();
        if (numbering.Count > 0)
            ResultTree.Items.Add(Group($"Gaps in numbering ({numbering.Count})", numbering.Select(ShowGapNode)));

        var sonarr = r.Sonarr.Where(g => Match(g.Show)).ToList();
        if (sonarr.Count > 0)
            ResultTree.Items.Add(Group($"Missing from Sonarr ({sonarr.Count})", sonarr.Select(SonarrGapNode)));

        var notFound = r.NotInSonarr.Where(Match).ToList();
        if (notFound.Count > 0)
            ResultTree.Items.Add(Group($"Not found in Sonarr ({notFound.Count})", notFound.Select(Leaf)));

        if (r.Errors.Count > 0)
            ResultTree.Items.Add(Group($"Errors ({r.Errors.Count})", r.Errors.Select(Leaf)));

        if (ResultTree.Items.Count == 0)
            AddNote(filter.Length > 0 ? "No files match this filter." : "No gaps found in the loaded files.");
    }

    TreeViewItem SeriesNode(SeriesGap s)
    {
        var meta = new List<string> { s.SeriesType };
        if (s.Year is int year) meta.Add(year.ToString());
        if (s.Network.Length > 0) meta.Add(s.Network);
        if (s.Status.Length > 0) meta.Add(s.Status);
        if (!s.Monitored) meta.Add("unmonitored");

        var counts = $"{s.MissingCount} missing" + (s.Downloading > 0 ? $", {s.Downloading} downloading" : "");
        var node = Node($"{s.Title}  ({string.Join(" · ", meta)})  —  {counts}");

        foreach (var season in s.Seasons.Where(g => g.Missing.Count > 0))
        {
            var state = season.EntireSeason ? "entire season" : $"{season.Missing.Count} of {season.MonitoredAired}";
            var seasonNode = Node($"Season {season.Season}  —  {state}");
            foreach (var ep in season.Missing) seasonNode.Items.Add(Leaf(EpisodeLine(ep)));
            node.Items.Add(seasonNode);
        }
        return node;
    }

    static TreeViewItem ShowGapNode(IGrouping<string, FileNumberingGap> group)
    {
        var node = Node(group.Key, true);
        foreach (var gap in group)
        {
            node.Items.Add(Leaf($"Season {gap.Season}  —  missing " + string.Join(", ", gap.Missing.Select(n => $"E{n:00}"))));
        }
        return node;
    }

    static TreeViewItem SonarrGapNode(FileSonarrGap gap)
    {
        var node = Node($"{gap.Show}  —  {gap.Missing.Count} missing");
        foreach (var season in gap.Missing.GroupBy(m => m.Season))
        {
            var seasonNode = Node($"Season {season.Key}  —  {season.Count()} missing");
            foreach (var ep in season) seasonNode.Items.Add(Leaf(EpisodeLine(ep)));
            node.Items.Add(seasonNode);
        }
        return node;
    }

    static string EpisodeLine(MissingEpisode ep)
    {
        var line = $"S{ep.Season:00}E{ep.Number:00}  {ep.Title}";
        if (!string.IsNullOrEmpty(ep.AirDate)) line += $"  ·  aired {ep.AirDate}";
        return line;
    }

    static string DriveText(GapReport r) => string.Join("   ", r.Drives.Select(d =>
    {
        var text = $"{d.Root} {GapAnalyzer.FormatSize(d.LibraryBytes)}";
        if (d.TotalBytes is long total)
        {
            text += $" of {GapAnalyzer.FormatSize(total)}";
            if (d.FreeBytes is long free) text += $" ({GapAnalyzer.FormatSize(free)} free)";
        }
        return text;
    }));

    static TreeViewItem Node(string header, bool expanded = false) => new() { Header = header, IsExpanded = expanded };

    static TreeViewItem Leaf(string header) => new() { Header = header };

    static TreeViewItem Group(string header, IEnumerable<TreeViewItem> children)
    {
        var node = Node(header, true);
        foreach (var child in children) node.Items.Add(child);
        return node;
    }

    void AddNote(string text) => ResultTree.Items.Add(Leaf(text));
}
