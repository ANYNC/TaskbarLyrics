using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Media = System.Windows.Media;

namespace TaskbarLyrics.Light.App;

public partial class SpectrumTuningWindow : Window
{
    private readonly Action<SpectrumTuningSettings> _apply;
    private readonly DispatcherTimer _diagnosticsTimer;
    private bool _isLoading;

    public SpectrumTuningSettings Settings { get; private set; }

    public SpectrumTuningWindow(SpectrumTuningSettings settings, Action<SpectrumTuningSettings> apply)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        Settings = settings.Clone();
        _apply = apply;
        BuildSliders();
        ApplyCurrent();
        _diagnosticsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _diagnosticsTimer.Tick += DiagnosticsTimer_Tick;
        _diagnosticsTimer.Start();
        UpdateDiagnosticsText();
    }

    public void ApplyExternalSettings(SpectrumTuningSettings settings)
    {
        Settings = settings.Clone();
        BuildSliders();
    }

    protected override void OnClosed(EventArgs e)
    {
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Tick -= DiagnosticsTimer_Tick;
        base.OnClosed(e);
    }

    private void DiagnosticsTimer_Tick(object? sender, EventArgs e)
    {
        UpdateDiagnosticsText();
    }

    private void UpdateDiagnosticsText()
    {
        var snapshot = SpectrumDiagnosticsState.Current;
        var lastAudio = snapshot.LastAudioUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "-";
        DiagnosticsText.Text =
            $"频谱模式: {FormatBool(snapshot.IsPureMusicMode)}    播放中: {FormatBool(snapshot.IsPlaying)}    采集可用: {FormatBool(snapshot.IsCaptureAvailable)}\n" +
            $"输入峰值: {snapshot.InputPeak:0.0000}    输出峰值: {snapshot.OutputPeak:0.0000}    格式: {snapshot.Format}\n" +
            $"最近音频: {lastAudio}    错误: {(string.IsNullOrWhiteSpace(snapshot.LastError) ? "-" : snapshot.LastError)}";
    }

    private static string FormatBool(bool value) => value ? "是" : "否";

    private void BuildSliders()
    {
        _isLoading = true;
        SlidersPanel.Children.Clear();

        AddSlider("FFT 窗口", "采样", 512, 2048, 512, Settings.SampleWindow, v => Settings.SampleWindow = CoerceSampleWindow(v));
        AddSlider("更新间隔", "ms", 16, 100, 1, Settings.UpdateIntervalMs, v => Settings.UpdateIntervalMs = (int)Math.Round(v));
        AddSlider("最低频率", "Hz", 20, 180, 1, Settings.MinFrequency, v => Settings.MinFrequency = v);
        AddSlider("最高频率", "Hz", 3000, 18000, 100, Settings.MaxFrequency, v => Settings.MaxFrequency = v);
        AddSlider("峰值初始值", "", 0.004, 0.16, 0.001, Settings.PeakInitial, v => Settings.PeakInitial = v);
        AddSlider("峰值衰减", "", 0.85, 0.995, 0.001, Settings.PeakDecay, v => Settings.PeakDecay = v);
        AddSlider("峰值下限", "", 0.003, 0.08, 0.001, Settings.PeakFloor, v => Settings.PeakFloor = v);
        AddSlider("峰值上限", "", 0.04, 1.0, 0.01, Settings.PeakCeiling, v => Settings.PeakCeiling = v);
        AddSlider("噪声门", "", 0, 0.2, 0.001, Settings.NoiseFloor, v => Settings.NoiseFloor = v);
        AddSlider("输出曲线", "", 0.25, 1.5, 0.01, Settings.OutputCurve, v => Settings.OutputCurve = v);
        AddSlider("低频增益", "", 0.2, 3.5, 0.01, Settings.LowBandGain, v => Settings.LowBandGain = v);
        AddSlider("频段增益斜率", "", -0.04, 0.10, 0.001, Settings.BandGainStep, v => Settings.BandGainStep = v);
        AddSlider("频率权重基准", "", 0.2, 2.2, 0.01, Settings.FrequencyWeightBase, v => Settings.FrequencyWeightBase = v);
        AddSlider("频率权重斜率", "", -0.08, 0.16, 0.001, Settings.FrequencyWeightSlope, v => Settings.FrequencyWeightSlope = v);
        AddSlider("后端上升速度", "", 0.05, 1.0, 0.01, Settings.BackendAttack, v => Settings.BackendAttack = v);
        AddSlider("后端下降速度", "", 0.02, 1.0, 0.01, Settings.BackendRelease, v => Settings.BackendRelease = v);
        AddSlider("前端上升速度", "", 0.02, 1.0, 0.01, Settings.FrontendRise, v => Settings.FrontendRise = v);
        AddSlider("前端下降速度", "", 0.02, 1.0, 0.01, Settings.FrontendFall, v => Settings.FrontendFall = v);
        AddSlider("最小柱高", "px", 1, 14, 0.5, Settings.MinBarHeight, v => Settings.MinBarHeight = v);
        AddSlider("柱高范围", "px", 4, 36, 0.5, Settings.BarHeightRange, v => Settings.BarHeightRange = v);
        AddSlider("柱子透明度", "", 0.2, 1.0, 0.01, Settings.BarOpacity, v => Settings.BarOpacity = v);

        _isLoading = false;
    }

    private void AddSlider(
        string label,
        string suffix,
        double minimum,
        double maximum,
        double tickFrequency,
        double value,
        Action<double> update)
    {
        var valueText = new TextBlock
        {
            Width = 82,
            TextAlignment = TextAlignment.Right,
            Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(203, 213, 225)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = tickFrequency,
            Value = value,
            IsSnapToTickEnabled = tickFrequency >= 1,
            Tag = suffix
        };

        void RefreshValueText()
        {
            valueText.Text = FormatValue(slider.Value, suffix);
        }

        slider.ValueChanged += (_, _) =>
        {
            var next = label == "FFT 窗口" ? CoerceSampleWindow(slider.Value) : slider.Value;
            if (label == "FFT 窗口" && Math.Abs(slider.Value - next) > 0.1)
            {
                slider.Value = next;
                return;
            }

            update(next);
            RefreshValueText();
            if (!_isLoading)
            {
                ApplyCurrent();
            }
        };

        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(226, 232, 240))
        };

        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valueText, 2);
        row.Children.Add(labelText);
        row.Children.Add(slider);
        row.Children.Add(valueText);
        SlidersPanel.Children.Add(row);
        RefreshValueText();
    }

    private void ApplyCurrent()
    {
        _apply(Settings.Clone());
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        Settings = SpectrumTuningSettings.CreateDefault();
        BuildSliders();
        ApplyCurrent();
    }

    private static int CoerceSampleWindow(double value)
    {
        return value switch
        {
            <= 768 => 512,
            <= 1536 => 1024,
            _ => 2048
        };
    }

    private static string FormatValue(double value, string suffix)
    {
        var text = Math.Abs(value) >= 100
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(suffix) ? text : $"{text} {suffix}";
    }
}
