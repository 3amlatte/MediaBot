using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediaRenamer.Services;

namespace MediaRenamer;

public partial class MatchWindow : Window
{
    public class MatchRow : INotifyPropertyChanged
    {
        ImageSource? _poster;

        public MatchRow(SearchResult item) => Item = item;

        public SearchResult Item { get; }
        public string Title => Item.Title;
        public string Subtitle => $"{(Item.Kind == "tv" ? "TV" : "Movie")}{(Item.Year is null ? "" : " · " + Item.Year)}";

        public ImageSource? Poster
        {
            get => _poster;
            set
            {
                _poster = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Poster)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    readonly MetadataService _meta;
    readonly int? _year;

    public SearchResult? Result { get; private set; }

    public MatchWindow(MetadataService meta, string query, bool tvMode, int? year)
    {
        InitializeComponent();
        _meta = meta;
        _year = year;
        QueryBox.Text = query;
        TvRadio.IsChecked = tvMode;
        MovieRadio.IsChecked = !tvMode;
        Loaded += async (_, _) => await SearchAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    async Task SearchAsync()
    {
        var query = QueryBox.Text.Trim();
        if (query.Length == 0)
        {
            InfoText.Text = "Enter a title to search for.";
            return;
        }

        try
        {
            InfoText.Text = "Searching...";
            var search = TvRadio.IsChecked == true
                ? await _meta.SearchTvAsync(query)
                : await _meta.SearchMoviesAsync(query, _year);

            var rows = search.Results.Select(r => new MatchRow(r)).ToList();
            ResultsBox.ItemsSource = rows;
            ResultsBox.SelectedIndex = rows.Count > 0 ? 0 : -1;

            if (rows.Count == 0)
                InfoText.Text = "No results found. Try a shorter show or movie title.";
            else if (!search.Query.Equals(query, StringComparison.OrdinalIgnoreCase))
                InfoText.Text = $"Found by searching for \u201c{search.Query}\u201d. Pick a match, then click OK.";
            else
                InfoText.Text = "Pick a match, then click OK.";

            await LoadPostersAsync(rows);
        }
        catch (Exception ex)
        {
            InfoText.Text = "Search failed: " + ex.Message;
        }
    }

    static async Task LoadPostersAsync(IEnumerable<MatchRow> rows)
    {
        var tasks = rows
            .Where(r => r.Item.PosterPath is not null)
            .Select(async r =>
            {
                var url = MetadataService.PosterUrl(r.Item, "w185");
                if (url.Length > 0) r.Poster = await ImageLoader.LoadAsync(url);
            });
        await Task.WhenAll(tasks);
    }

    async void ResultsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsBox.SelectedItem is not MatchRow row)
        {
            PreviewImage.Source = null;
            PreviewText.Text = "";
            return;
        }

        PreviewImage.Source = row.Poster;
        PreviewText.Text = row.Poster is null ? "No artwork" : "";

        if (row.Item.PosterPath is null) return;
        var url = MetadataService.PosterUrl(row.Item, "w342");
        var large = url.Length == 0 ? null : await ImageLoader.LoadAsync(url);
        if (large is not null && ResultsBox.SelectedItem == row)
        {
            PreviewImage.Source = large;
            PreviewText.Text = "";
        }
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsBox.SelectedItem is not MatchRow row)
        {
            InfoText.Text = "Select a result first.";
            return;
        }
        Result = row.Item;
        DialogResult = true;
    }

    void ResultsBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsBox.SelectedItem is MatchRow) Ok_Click(sender, e);
    }
}
