using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace TaskbarLyrics.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        NeteaseCheckBox.IsChecked = _settings.EnableNetease;
        QQMusicCheckBox.IsChecked = _settings.EnableQQMusic;
        StartupCheckBox.IsChecked = _settings.ShowLyricsOnStartup;
        BackgroundCheckBox.IsChecked = _settings.ShowBackground;
        BorderCheckBox.IsChecked = _settings.ShowBorder;

        FontSizeTextBox.Text = _settings.FontSize.ToString(CultureInfo.InvariantCulture);
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
        _settings.EnableNetease = NeteaseCheckBox.IsChecked == true;
        _settings.EnableQQMusic = QQMusicCheckBox.IsChecked == true;
        _settings.ShowLyricsOnStartup = StartupCheckBox.IsChecked == true;
        _settings.ShowBackground = BackgroundCheckBox.IsChecked == true;
        _settings.ShowBorder = BorderCheckBox.IsChecked == true;

        _settings.FontSize = ParseDouble(FontSizeTextBox, 14, 10, 40);
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

    private static double ParseDouble(System.Windows.Controls.TextBox input, double fallback, double min, double max)
    {
        if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
