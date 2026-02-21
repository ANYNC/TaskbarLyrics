using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskbarLyrics.Adapters.Netease;
using TaskbarLyrics.Adapters.QQMusic;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<RecognitionSourceItem> _recognitionOrderItems = new();
    private System.Windows.Point _recognitionDragStartPoint;
    private RecognitionSourceItem? _draggedRecognitionItem;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        PopulateFontFamilyOptions();
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        LoadRecognitionOrder(_settings.SourceRecognitionOrder);
        StartupCheckBox.IsChecked = _settings.ShowLyricsOnStartup;
        BackgroundCheckBox.IsChecked = _settings.ShowBackground;
        BorderCheckBox.IsChecked = _settings.ShowBorder;
        LyricMismatchResolverCheckBox.IsChecked = _settings.EnableLyricMismatchResolver;
        SmtcTimelineMonitorCheckBox.IsChecked = _settings.EnableSmtcTimelineMonitor;

        FontSizeTextBox.Text = _settings.FontSize.ToString(CultureInfo.InvariantCulture);
        FontFamilyComboBox.Text = ExtractPrimaryFontFamily(_settings.FontFamily);
        FontWeightComboBox.SelectedIndex = NormalizeFontWeight(_settings.FontWeight) switch
        {
            "Light" => 0,
            "Normal" => 1,
            "Medium" => 2,
            "SemiBold" => 3,
            "Bold" => 4,
            _ => 1
        };
        ForegroundColorTextBox.Text = _settings.ForegroundColor;
        BackgroundOpacityTextBox.Text = _settings.BackgroundOpacity.ToString(CultureInfo.InvariantCulture);
        WindowWidthTextBox.Text = _settings.WindowWidth.ToString(CultureInfo.InvariantCulture);
        XOffsetTextBox.Text = _settings.XOffset.ToString(CultureInfo.InvariantCulture);
        YOffsetTextBox.Text = _settings.YOffset.ToString(CultureInfo.InvariantCulture);

        var anchor = _settings.HorizontalAnchor.ToString();
        AnchorComboBox.SelectedIndex = anchor switch
        {
            "Left" => 0,
            "Center" => 1,
            _ => 2
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.SourceRecognitionOrder = _recognitionOrderItems.Select(x => x.SourceKey).ToList();
        _settings.ShowLyricsOnStartup = StartupCheckBox.IsChecked == true;
        _settings.ShowBackground = BackgroundCheckBox.IsChecked == true;
        _settings.ShowBorder = BorderCheckBox.IsChecked == true;
        _settings.EnableLyricMismatchResolver = LyricMismatchResolverCheckBox.IsChecked == true;
        _settings.EnableSmtcTimelineMonitor = SmtcTimelineMonitorCheckBox.IsChecked == true;

        _settings.FontSize = ParseDouble(FontSizeTextBox, 14, 10, 40);
        _settings.FontFamily = string.IsNullOrWhiteSpace(FontFamilyComboBox.Text)
            ? "SF Pro Display, SF Pro Text, Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei"
            : FontFamilyComboBox.Text.Trim();
        _settings.FontWeight = FontWeightComboBox.SelectedIndex switch
        {
            0 => "Light",
            1 => "Normal",
            2 => "Medium",
            3 => "SemiBold",
            4 => "Bold",
            _ => "Normal"
        };
        _settings.ForegroundColor = string.IsNullOrWhiteSpace(ForegroundColorTextBox.Text)
            ? "#FFFFFFFF"
            : ForegroundColorTextBox.Text.Trim();

        _settings.BackgroundOpacity = ParseDouble(BackgroundOpacityTextBox, 0.55, 0, 1);
        _settings.WindowWidth = ParseDouble(WindowWidthTextBox, 420, 260, 1200);
        _settings.XOffset = ParseDouble(XOffsetTextBox, 0, -2000, 2000);
        _settings.YOffset = ParseDouble(YOffsetTextBox, 0, -2000, 2000);

        _settings.HorizontalAnchor = AnchorComboBox.SelectedIndex switch
        {
            0 => LyricsHorizontalAnchor.Left,
            1 => LyricsHorizontalAnchor.Center,
            _ => LyricsHorizontalAnchor.Right
        };

        if (System.Windows.Application.Current is App app)
        {
            app.SaveSettings(_settings.Clone());
        }

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ClearLyricCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "清除歌词缓存后，首次重新匹配会变慢，是否继续？",
            "清除缓存",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        QQMusicLyricProvider.ClearCache();
        NeteaseLyricProvider.ClearCache();
        LrcLibLyricProvider.ClearCache();
        GenericSmtcLyricProvider.ClearCache();

        System.Windows.MessageBox.Show(
            "歌词缓存已清除。",
            "完成",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private static double ParseDouble(System.Windows.Controls.TextBox input, double fallback, double min, double max)
    {
        if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static string NormalizeFontWeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Normal";
        }

        return value.Trim() switch
        {
            "Light" => "Light",
            "Normal" => "Normal",
            "Medium" => "Medium",
            "SemiBold" => "SemiBold",
            "Bold" => "Bold",
            _ => "Normal"
        };
    }

    private void PopulateFontFamilyOptions()
    {
        FontFamilyComboBox.ItemsSource = Fonts.SystemFontFamilies
            .Select(x => x.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ExtractPrimaryFontFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return string.Empty;
        }

        var first = fontFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? fontFamily.Trim() : first;
    }

    private void LoadRecognitionOrder(IReadOnlyList<string>? configuredOrder)
    {
        _recognitionOrderItems.Clear();
        var ordered = NormalizeRecognitionOrder(configuredOrder);
        foreach (var key in ordered)
        {
            _recognitionOrderItems.Add(new RecognitionSourceItem(key, ToDisplayName(key)));
        }

        RecognitionOrderListBox.ItemsSource = _recognitionOrderItems;
    }

    private static List<string> NormalizeRecognitionOrder(IReadOnlyList<string>? configuredOrder)
    {
        var defaults = new[] { "QQMusic", "Netease", "Spotify" };
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (configuredOrder is not null)
        {
            foreach (var item in configuredOrder)
            {
                var normalized = NormalizeRecognitionSource(item);
                if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                {
                    result.Add(normalized);
                }
            }
        }

        foreach (var item in defaults)
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static string NormalizeRecognitionSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var trimmed = source.Trim();
        if (trimmed.Contains("qq", StringComparison.OrdinalIgnoreCase))
        {
            return "QQMusic";
        }

        if (trimmed.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase))
        {
            return "Netease";
        }

        if (trimmed.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return "Spotify";
        }

        return string.Empty;
    }

    private static string ToDisplayName(string source)
    {
        return source switch
        {
            "QQMusic" => "QQ音乐",
            "Netease" => "网易云音乐",
            "Spotify" => "Spotify",
            _ => source
        };
    }

    private void RecognitionOrderListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _recognitionDragStartPoint = e.GetPosition(null);
        _draggedRecognitionItem = FindRecognitionItem(e.OriginalSource as DependencyObject);
    }

    private void RecognitionOrderListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedRecognitionItem is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - _recognitionDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _recognitionDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(RecognitionOrderListBox, _draggedRecognitionItem, System.Windows.DragDropEffects.Move);
    }

    private void RecognitionOrderListBox_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(RecognitionSourceItem))
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void RecognitionOrderListBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(RecognitionSourceItem)))
        {
            return;
        }

        if (e.Data.GetData(typeof(RecognitionSourceItem)) is not RecognitionSourceItem sourceItem)
        {
            return;
        }

        var targetItem = FindRecognitionItem(e.OriginalSource as DependencyObject);
        if (targetItem is null || ReferenceEquals(sourceItem, targetItem))
        {
            return;
        }

        var fromIndex = _recognitionOrderItems.IndexOf(sourceItem);
        var toIndex = _recognitionOrderItems.IndexOf(targetItem);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
        {
            return;
        }

        _recognitionOrderItems.Move(fromIndex, toIndex);
        RecognitionOrderListBox.SelectedItem = sourceItem;
    }

    private RecognitionSourceItem? FindRecognitionItem(DependencyObject? origin)
    {
        var current = origin;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: RecognitionSourceItem item })
            {
                return item;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed record RecognitionSourceItem(string SourceKey, string DisplayName);
}
