using System.Windows;
using System.Windows.Controls;
using MediaRenamer.Models;
using MediaRenamer.Services;

namespace MediaRenamer;

public partial class EpisodesPanel : UserControl
{
    public record ShowRow(SearchResult Show)
    {
        public string Display => Show.Year is null ? Show.Title : $"{Show.Title} ({Show.Year})";
        public override string ToString() => Display;
    }

    public record SeasonRow(SeasonSummary Season)
    {
        public string Display => Season.Number == 0
            ? $"Specials ({Season.EpisodeCount})"
            : $"{Season.Name} ({Season.EpisodeCount} episodes)";
        public override string ToString() => Display;
    }

    public record EpisodeRow(string Code, string Title, string AirDate, string Have);

    readonly List<RenameItem> _items = new();
    AppSettings _settings = new();
    TmdbClient? _client;
    TvDetails? _show;

    public EpisodesPanel()
    {
        InitializeComponent();
    }

    public void Open(IEnumerable<RenameItem> items, AppSettings settings)
    {
        _settings = settings;
        _items.Clear();
        _items.AddRange(items.Where(i => !i.IsSubtitle));
        if (_show is not null) LoadSeason(SeasonCombo.SelectedItem as SeasonRow);
    }

    async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            SummaryText.Text = "Set your TMDB API key in Preferences first.";
            return;
        }

        var query = QueryBox.Text.Trim();
        if (query.Length == 0)
        {
            SummaryText.Text = "Enter a show name.";
            return;
        }

        try
        {
            SummaryText.Text = "Searching...";
            _client = new TmdbClient(_settings.ApiKey);
            var search = await _client.SearchTvSmartAsync(query);
            if (search.Results.Count == 0)
            {
                ShowCombo.ItemsSource = null;
                SeasonCombo.ItemsSource = null;
                EpisodeGrid.ItemsSource = null;
                SummaryText.Text = "No shows found. Try a shorter show title.";
                return;
            }

            ShowCombo.ItemsSource = search.Results.Select(r => new ShowRow(r)).ToList();
            ShowCombo.SelectedIndex = 0;

            var found = $"{search.Results.Count} show(s) found";
            SummaryText.Text = search.Query.Equals(query, StringComparison.OrdinalIgnoreCase)
                ? $"{found}. Pick one to list its episodes."
                : $"{found} by searching for \u201c{search.Query}\u201d. Pick one to list its episodes.";
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Search failed: " + ex.Message;
        }
    }

    async void ShowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShowCombo.SelectedItem is not ShowRow row || _client is null) return;

        try
        {
            SummaryText.Text = "Loading show...";
            _show = await _client.GetTvDetailsAsync(row.Show.Id);
            var seasons = _show.Seasons.Select(s => new SeasonRow(s)).ToList();
            SeasonCombo.ItemsSource = seasons;

            var first = seasons.FirstOrDefault(s => s.Season.Number > 0) ?? seasons.FirstOrDefault();
            SeasonCombo.SelectedItem = first;

            SummaryText.Text = $"{_show.Name} ({_show.Year})  ·  {_show.Seasons.Count} season(s)  ·  " +
                               $"{_show.Seasons.Sum(s => s.EpisodeCount)} episodes" +
                               (_show.Rating is double r ? $"  ·  TMDB {r:0.0}" : "");
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Could not load the show: " + ex.Message;
        }
    }

    async void SeasonCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSeason(SeasonCombo.SelectedItem as SeasonRow);
        await Task.CompletedTask;
    }

    async void LoadSeason(SeasonRow? season)
    {
        if (season is null || _show is null || _client is null)
        {
            EpisodeGrid.ItemsSource = null;
            return;
        }

        try
        {
            var episodes = await _client.GetSeasonEpisodesAsync(_show.Id, season.Season.Number);
            var rows = episodes
                .OrderBy(kv => kv.Key)
                .Select(kv => new EpisodeRow(
                    $"S{season.Season.Number:00}E{kv.Key:00}",
                    kv.Value.Name,
                    kv.Value.AirDate,
                    HaveEpisode(season.Season.Number, kv.Key) ? "yes" : ""))
                .ToList();

            EpisodeGrid.ItemsSource = rows;
            var have = rows.Count(r => r.Have.Length > 0);
            SummaryText.Text = $"{_show.Name}  ·  {season.Display}  ·  {have} of {rows.Count} episode(s) loaded here.";
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Could not load episodes: " + ex.Message;
        }
    }

    bool HaveEpisode(int season, int episode) =>
        _items.Any(i => i.Parsed.Season == season && i.Parsed.Episode == episode);
}
