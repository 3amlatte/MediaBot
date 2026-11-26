using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MediaRenamer.Models;
using MediaRenamer.Services;
using Microsoft.Win32;

namespace MediaRenamer;

public partial class ListPanel : UserControl
{
    readonly List<RenameItem> _items = new();
    SearchResult? _match;
    List<string> _lines = new();
    bool _ready;

    public ListPanel()
    {
        InitializeComponent();
        TemplateBox.Text = "{original} -> {new}";
        _ready = true;
        Refresh();
    }

    public void Open(IEnumerable<RenameItem> items, SearchResult? match)
    {
        _match = match;
        AddItems(items);
    }

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = FileCollector.Filter };
        if (dlg.ShowDialog() == true) AddItems(FileCollector.Collect(dlg.FileNames).Items);
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        Refresh();
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
        var known = _items.Select(i => i.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!known.Add(item.SourcePath)) continue;
            _items.Add(item);
        }
        Refresh();
    }

    void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        TemplateBox.IsEnabled = CsvMode.IsChecked != true;
        Refresh();
    }

    void Template_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) Refresh();
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(string.Join(Environment.NewLine, _lines));
        InfoText.Text = "Copied to the clipboard.";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var csv = CsvMode.IsChecked == true;
        var dlg = new SaveFileDialog
        {
            Filter = csv ? "CSV file|*.csv" : "Text file|*.txt",
            FileName = csv ? "list.csv" : "list.txt"
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, string.Join(Environment.NewLine, _lines), new UTF8Encoding(true));
        InfoText.Text = "Saved.";
    }

    void Refresh()
    {
        _lines = CsvMode.IsChecked == true
            ? ListExporter.Csv(_items, _match)
            : ListExporter.Text(_items, TemplateBox.Text, _match);

        PreviewList.ItemsSource = null;
        PreviewList.ItemsSource = _lines;
        InfoText.Text = _items.Count == 0
            ? "No files yet. Add files or drag them into this panel."
            : $"{_items.Count} file(s)";
    }
}
