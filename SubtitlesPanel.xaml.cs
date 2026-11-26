using System.IO;
using System.Windows;
using System.Windows.Controls;
using MediaRenamer.Models;
using MediaRenamer.Services;
using Microsoft.Win32;

namespace MediaRenamer;

public partial class SubtitlesPanel : UserControl
{
    public class SubtitleRow
    {
        public SubtitleRow(RenameItem video) => Video = video;

        public RenameItem Video { get; }
        public string? LocalPath { get; set; }
        public SubtitleResult? Online { get; set; }
        public string Status { get; set; } = "";

        public string Display
        {
            get
            {
                var source = LocalPath is not null ? Path.GetFileName(LocalPath)
                    : Online is not null ? $"{Online.FileName} ({Online.Downloads} downloads)"
                    : "no subtitle";
                return $"{Video.OriginalName}  →  {source}  [{Status}]";
            }
        }
    }

    readonly List<SubtitleRow> _rows = new();
    AppSettings _settings = new();
    OpenSubtitlesClient? _client;
    bool _opened;

    public SubtitlesPanel()
    {
        InitializeComponent();
        ConvertBox.IsChecked = true;
        LangCombo.ItemsSource = Languages.All;
    }

    string SelectedLanguage => (LangCombo.SelectedItem as LanguageOption)?.Code ?? "en";

    void SelectLanguage(string code)
    {
        LangCombo.SelectedItem = Languages.All.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            ?? Languages.All[0];
    }

    public void Open(IEnumerable<RenameItem> items, AppSettings settings)
    {
        _settings = settings;
        _client = null;
        if (!_opened)
        {
            SelectLanguage(settings.SubtitleLanguage);
            _opened = true;
        }
        AddItems(items);
    }

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = FileCollector.Filter };
        if (dlg.ShowDialog() == true) AddItems(FileCollector.Collect(dlg.FileNames).Items);
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
            AddItems(FileCollector.Collect(paths).Items);
            e.Handled = true;
        }
    }

    void AddItems(IEnumerable<RenameItem> items)
    {
        var known = _rows.Select(r => r.Video.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var item in items.Where(i => !i.IsSubtitle))
        {
            if (!known.Add(item.SourcePath)) continue;
            _rows.Add(new SubtitleRow(item));
            added++;
        }
        Refresh();
        if (added > 0) InfoText.Text = $"Added {added} video file(s).";
    }

    void FindLocal_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.Online = null;
            row.LocalPath = SubtitleMatcher.FindLocal(row.Video);
            row.Status = row.LocalPath is null ? "no local subtitle" : "local match";
        }
        Refresh();
        InfoText.Text = $"Found local subtitles for {_rows.Count(r => r.LocalPath is not null)} of {_rows.Count} file(s).";
    }

    async void SearchOnline_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            InfoText.Text = "Add video files first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_settings.OpenSubsApiKey))
        {
            InfoText.Text = "Set your OpenSubtitles API key in Preferences first.";
            return;
        }
        var lang = SelectedLanguage;
        if (lang.Length == 0)
        {
            InfoText.Text = "Enter a language code, such as en.";
            return;
        }

        var client = GetClient();
        foreach (var row in _rows)
        {
            row.LocalPath = null;
            row.Online = null;
            row.Status = "searching...";
            Refresh();
            try
            {
                row.Online = await FindOnlineAsync(client, row.Video, lang);
                row.Status = row.Online is null ? "no online match" : "online match";
            }
            catch (Exception ex)
            {
                row.Status = "error: " + ex.Message;
            }
            Refresh();
        }
        InfoText.Text = $"Found online subtitles for {_rows.Count(r => r.Online is not null)} of {_rows.Count} file(s).";
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var lang = SelectedLanguage;
        var convert = ConvertBox.IsChecked == true;
        int applied = 0;

        foreach (var row in _rows)
        {
            try
            {
                if (row.LocalPath is not null)
                {
                    row.LocalPath = SubtitleMatcher.Apply(row.Video, row.LocalPath, convert);
                    row.Status = "renamed";
                    applied++;
                }
                else if (row.Online is not null)
                {
                    var bytes = await GetClient().DownloadAsync(row.Online.FileId);
                    var path = Path.Combine(Path.GetDirectoryName(row.Video.SourcePath)!, row.Video.Stem + "." + lang + ".srt");
                    await File.WriteAllBytesAsync(path, bytes);
                    if (convert) SubtitleMatcher.ConvertToUtf8(path);
                    row.Status = "saved";
                    applied++;
                }
            }
            catch (Exception ex)
            {
                row.Status = "failed: " + ex.Message;
            }
            Refresh();
        }

        InfoText.Text = $"Applied {applied} subtitle(s).";
    }

    static async Task<SubtitleResult?> FindOnlineAsync(OpenSubtitlesClient client, RenameItem video, string lang)
    {
        var results = new List<SubtitleResult>();
        try
        {
            var (hash, size) = OpenSubtitlesClient.ComputeHash(video.SourcePath);
            results = await client.SearchAsync(lang, null, null, null, hash, size);
        }
        catch (IOException)
        {
        }

        if (results.Count == 0)
        {
            var p = video.Parsed;
            results = await client.SearchAsync(lang, p.Title, p.Season, p.Episode, null, null);
        }
        return results.FirstOrDefault();
    }

    OpenSubtitlesClient GetClient() =>
        _client ??= new OpenSubtitlesClient(_settings.OpenSubsApiKey, _settings.OpenSubsUser, _settings.OpenSubsPassword);

    void Refresh()
    {
        SubsList.ItemsSource = null;
        SubsList.ItemsSource = _rows.ToList();
        if (_rows.Count == 0) InfoText.Text = "No video files yet. Add files or drag them into this panel.";
    }
}
