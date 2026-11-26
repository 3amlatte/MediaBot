using System.Windows;
using MediaRenamer.Services;
using Microsoft.Win32;

namespace MediaRenamer;

public partial class SettingsWindow : Window
{
    readonly AppSettings _current;

    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _current = current;
        Result = current;

        SourceCombo.ItemsSource = new[] { "TMDB", "TheTVDB" };
        SourceCombo.SelectedItem = current.MetadataSource.Equals("tvdb", StringComparison.OrdinalIgnoreCase) ? "TheTVDB" : "TMDB";
        ApiKeyBox.Text = current.ApiKey;
        TvdbKeyBox.Text = current.TvdbApiKey;
        TvdbPinBox.Text = current.TvdbPin;
        WriteMetadataBox.IsChecked = current.WriteMetadata;
        TvTemplateBox.Text = current.TvTemplate;
        MovieTemplateBox.Text = current.MovieTemplate;
        OutputBox.Text = current.OutputFolder;
        TvFolderBox.Text = current.TvFolder;
        MovieFolderBox.Text = current.MovieFolder;
        SonarrUrlBox.Text = current.SonarrUrl;
        SonarrKeyBox.Text = current.SonarrApiKey;
        SonarrSpecialsBox.IsChecked = current.SonarrIncludeSpecials;
        SonarrMonitoredBox.IsChecked = current.SonarrMonitoredOnly;
        OpenSubsKeyBox.Text = current.OpenSubsApiKey;
        OpenSubsUserBox.Text = current.OpenSubsUser;
        OpenSubsPasswordBox.Password = current.OpenSubsPassword;
        SubtitleLanguageBox.Text = current.SubtitleLanguage;
        DryRunBox.IsChecked = current.DryRun;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    void Plex_Click(object sender, RoutedEventArgs e)
    {
        TvTemplateBox.Text = "{n} - S{s}E{e} - {t}";
        MovieTemplateBox.Text = "{n} ({y})";
        TvFolderBox.Text = "{n}/Season {s}";
        MovieFolderBox.Text = "{n} ({y})";
    }

    void DetectSonarr_Click(object sender, RoutedEventArgs e)
    {
        var found = SonarrLocator.Detect();
        if (found is null)
        {
            MessageBox.Show(this, "No Sonarr install was found on this PC. Enter the URL and API key yourself.",
                "MediaBot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SonarrUrlBox.Text = found.Url;
        SonarrKeyBox.Text = found.ApiKey;
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) == true) OutputBox.Text = dlg.FolderName;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = _current with
        {
            MetadataSource = (string)SourceCombo.SelectedItem == "TheTVDB" ? "tvdb" : "tmdb",
            ApiKey = ApiKeyBox.Text.Trim(),
            TvdbApiKey = TvdbKeyBox.Text.Trim(),
            TvdbPin = TvdbPinBox.Text.Trim(),
            WriteMetadata = WriteMetadataBox.IsChecked == true,
            TvTemplate = TvTemplateBox.Text,
            MovieTemplate = MovieTemplateBox.Text,
            OutputFolder = OutputBox.Text.Trim(),
            TvFolder = TvFolderBox.Text,
            MovieFolder = MovieFolderBox.Text,
            SonarrUrl = SonarrUrlBox.Text.Trim(),
            SonarrApiKey = SonarrKeyBox.Text.Trim(),
            SonarrIncludeSpecials = SonarrSpecialsBox.IsChecked == true,
            SonarrMonitoredOnly = SonarrMonitoredBox.IsChecked == true,
            OpenSubsApiKey = OpenSubsKeyBox.Text.Trim(),
            OpenSubsUser = OpenSubsUserBox.Text.Trim(),
            OpenSubsPassword = OpenSubsPasswordBox.Password,
            SubtitleLanguage = SubtitleLanguageBox.Text.Trim(),
            DryRun = DryRunBox.IsChecked == true
        };
        DialogResult = true;
    }
}
