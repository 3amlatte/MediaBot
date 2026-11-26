using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaRenamer.Models;
using MediaRenamer.Services;
using Microsoft.Win32;

namespace MediaRenamer;

public partial class MainWindow : Window
{
    readonly List<RenameItem> _items = new();
    readonly List<RenameBatch> _history = UndoHistory.Load();
    AppSettings _settings = SettingsStore.Load();
    SearchResult? _match;

    public MainWindow()
    {
        InitializeComponent();
        Refresh();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Media files|*.mkv;*.mp4;*.avi;*.m4v;*.mov;*.wmv;*.ts;*.webm;*.srt;*.sub;*.ass;*.ssa;*.vtt|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true) AddPaths(dlg.FileNames);
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        _match = null;
        Refresh();
        Status("Cleared.");
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        var idx = OriginalList.SelectedIndex;
        if (idx < 0) return;
        _items.RemoveAt(idx);
        Refresh(Math.Min(idx, _items.Count - 1));
    }

    void Up_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    void MoveSelected(int delta)
    {
        var idx = OriginalList.SelectedIndex;
        var target = idx + delta;
        if (idx < 0 || target < 0 || target >= _items.Count) return;
        (_items[idx], _items[target]) = (_items[target], _items[idx]);
        Refresh(target);
    }

    void OriginalList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        NewList.SelectedIndex = OriginalList.SelectedIndex;
    }

    async void Fetch_Click(object sender, RoutedEventArgs e) => await FetchAsync();

    async void Match_Click(object sender, RoutedEventArgs e)
    {
        if (_match is null)
        {
            await FetchAsync();
            if (_match is null) return;
        }
        await BuildAsync(_match);
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0) return;
        Clipboard.SetText(string.Join(Environment.NewLine, _items.Select(i => i.NewName)));
        Status("Copied new names to the clipboard.");
    }

    async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var ready = _items.Count(i => i.Status == "Ready");
        if (ready == 0)
        {
            Status("Nothing to rename. Click Match first.");
            return;
        }

        if (_settings.DryRun)
        {
            Status($"Dry run: {ready} file(s) would be renamed. Nothing was changed.");
            return;
        }

        var confirm = MessageBox.Show(this, $"Rename {ready} file(s)?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var batch = Renamer.Apply(_items);
        if (batch.Moves.Count > 0)
        {
            if (_settings.WriteMetadata)
            {
                Status("Writing NFO files and artwork...");
                batch.CreatedFiles?.AddRange(await MetadataWriter.WriteAsync(_items));
            }
            _history.Add(batch);
            UndoHistory.Save(_history);
        }
        Refresh();
        Status($"Done. Renamed {batch.Moves.Count} file(s).");
    }

    void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0)
        {
            Status("Nothing to undo.");
            return;
        }

        var batch = _history[^1];
        var confirm = MessageBox.Show(this,
            $"Undo the last rename ({batch.Moves.Count} file(s) from {batch.Time:g})?", "Confirm Undo",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var restored = Renamer.Undo(batch);
        foreach (var move in restored)
        {
            var item = _items.FirstOrDefault(i => string.Equals(i.SourcePath, move.To, StringComparison.OrdinalIgnoreCase));
            if (item is null) continue;
            item.SourcePath = move.From;
            item.Parsed = FilenameParser.Parse(move.From);
            item.NewName = item.OriginalName;
            item.Status = "Undone";
        }

        if (batch.Moves.Count == 0) _history.Remove(batch);
        UndoHistory.Save(_history);
        Refresh();

        Status(batch.Moves.Count == 0
            ? $"Restored {restored.Count} file(s)."
            : $"Restored {restored.Count} file(s). {batch.Moves.Count} could not be restored (moved, or a file now exists at the original path).");
    }

    void RenameNav_Click(object sender, RoutedEventArgs e) => ShowView(RenameView, RenameNav);

    void Subtitles_Click(object sender, RoutedEventArgs e)
    {
        SubtitlesPanel.Open(_items, _settings);
        ShowView(SubtitlesPanel, SubtitlesNav);
    }

    void List_Click(object sender, RoutedEventArgs e)
    {
        ListPanel.Open(_items, _match);
        ShowView(ListPanel, ListNav);
    }

    void Missing_Click(object sender, RoutedEventArgs e)
    {
        MissingPanel.Open(_items, _settings);
        ShowView(MissingPanel, MissingNav);
    }

    void Sfv_Click(object sender, RoutedEventArgs e)
    {
        SfvPanel.Open(_items);
        ShowView(SfvPanel, SfvNav);
    }

    void Episodes_Click(object sender, RoutedEventArgs e)
    {
        EpisodesPanel.Open(_items, _settings);
        ShowView(EpisodesPanel, EpisodesNav);
    }

    void ShowView(UIElement view, Button nav)
    {
        RenameView.Visibility = view == RenameView ? Visibility.Visible : Visibility.Collapsed;
        SubtitlesPanel.Visibility = view == SubtitlesPanel ? Visibility.Visible : Visibility.Collapsed;
        ListPanel.Visibility = view == ListPanel ? Visibility.Visible : Visibility.Collapsed;
        MissingPanel.Visibility = view == MissingPanel ? Visibility.Visible : Visibility.Collapsed;
        SfvPanel.Visibility = view == SfvPanel ? Visibility.Visible : Visibility.Collapsed;
        EpisodesPanel.Visibility = view == EpisodesPanel ? Visibility.Visible : Visibility.Collapsed;

        foreach (var button in new[] { RenameNav, EpisodesNav, SubtitlesNav, ListNav, MissingNav, SfvNav })
        {
            button.Background = button == nav
                ? (Brush)FindResource("SelectedBg")
                : Brushes.Transparent;
        }
    }

    void Preferences_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        _settings = dlg.Result;
        SettingsStore.Save(_settings);
        Refresh();
        Status("Preferences saved.");
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }

    void AddPaths(IEnumerable<string> paths)
    {
        var result = FileCollector.Collect(paths);
        var known = _items.Select(i => i.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var item in result.Items)
        {
            if (!known.Add(item.SourcePath)) continue;
            _items.Add(item);
            added++;
        }

        Refresh();
        Status(result.Skipped > 0
            ? $"Loaded {added} media file(s). Skipped {result.Skipped} other file(s)."
            : $"Loaded {added} media file(s).");
    }

    async Task FetchAsync()
    {
        var client = GetClient();
        if (client is null) return;

        var videos = _items.Where(i => !i.IsSubtitle).ToList();
        if (videos.Count == 0)
        {
            Status("Add video files first.");
            return;
        }

        var dlg = new MatchWindow(client, videos[0].Parsed.Title, DetectTvMode(), videos[0].Parsed.Year) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is { } result)
        {
            _match = result;
            Status($"Fetched {result}. Click Match to apply it to the files.");
        }
    }

    async Task BuildAsync(SearchResult match)
    {
        var client = GetClient();
        if (client is null) return;

        try
        {
            var episodes = new Dictionary<int, Dictionary<int, EpisodeInfo>>();
            if (match.Kind == "tv")
            {
                Status("Fetching episode titles...");
                var seasons = _items
                    .Where(i => !i.IsSubtitle && i.Parsed.Season is not null)
                    .Select(i => i.Parsed.Season!.Value)
                    .Distinct();
                foreach (var s in seasons)
                    episodes[s] = await client.GetSeasonEpisodesAsync(match.Id, s);
            }

            Renamer.Build(_items, match, episodes, GetOptions());
            if (_settings.WriteMetadata) await AttachMetadataAsync(client, match, episodes);
            await ShowMatchPosterAsync(match);
            Refresh();
            Status("Review the new names and destinations, then click Rename.");
        }
        catch (Exception ex)
        {
            Status("Match failed: " + ex.Message);
        }
    }

    async Task ShowMatchPosterAsync(SearchResult match)
    {
        if (match.PosterPath is null)
        {
            MatchPoster.Source = null;
            return;
        }
        MatchPoster.Source = await ImageLoader.LoadAsync(TmdbClient.ImageUrl(match.PosterPath, "w342"));
    }

    async Task AttachMetadataAsync(TmdbClient client, SearchResult match,
        Dictionary<int, Dictionary<int, EpisodeInfo>> episodes)
    {
        var ready = _items.Where(i => !i.IsSubtitle && i.Status == "Ready").ToList();

        if (match.Kind == "tv")
        {
            Status("Fetching show metadata and artwork...");
            var show = await client.GetTvDetailsAsync(match.Id);
            var showNfo = MetadataWriter.TvShowNfo(show);
            var poster = show.PosterPath is null ? null : TmdbClient.ImageUrl(show.PosterPath);

            foreach (var item in ready)
            {
                item.FolderNfo = showNfo;
                item.FolderPosterUrl = poster;
                var p = item.Parsed;
                if (p.Season is int s && p.Episode is int ep
                    && episodes.TryGetValue(s, out var eps) && eps.TryGetValue(ep, out var info))
                {
                    item.Nfo = MetadataWriter.EpisodeNfo(match.Title, s, ep, info);
                }
            }
        }
        else
        {
            Status("Fetching movie metadata and artwork...");
            var movie = await client.GetMovieDetailsAsync(match.Id);
            var poster = movie.PosterPath is null ? null : TmdbClient.ImageUrl(movie.PosterPath);

            foreach (var item in ready)
            {
                item.Nfo = MetadataWriter.MovieNfo(movie);
                item.FolderPosterUrl = poster;
            }
        }
    }

    NamingOptions GetOptions() => new(
        _settings.TvTemplate,
        _settings.MovieTemplate,
        _settings.TvFolder,
        _settings.MovieFolder,
        string.IsNullOrWhiteSpace(_settings.OutputFolder) ? null : _settings.OutputFolder);

    bool DetectTvMode()
    {
        var videos = _items.Where(i => !i.IsSubtitle).ToList();
        return videos.Count > 0 && videos.Count(v => v.Parsed.Season is not null) * 2 > videos.Count;
    }

    TmdbClient? GetClient()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            Status("Set your TMDB API key in Preferences first.");
            return null;
        }
        return new TmdbClient(_settings.ApiKey);
    }

    void Refresh(int? selectIndex = null)
    {
        var idx = selectIndex ?? OriginalList.SelectedIndex;
        var list = _items.ToList();

        OriginalList.ItemsSource = null;
        OriginalList.ItemsSource = list;
        NewList.ItemsSource = null;
        NewList.ItemsSource = list;
        if (idx >= 0 && idx < list.Count) OriginalList.SelectedIndex = idx;

        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OutputText.Text = "Output: " + (string.IsNullOrWhiteSpace(_settings.OutputFolder)
            ? "rename in place"
            : _settings.OutputFolder);
    }

    void Status(string message) => StatusText.Text = message;
}
