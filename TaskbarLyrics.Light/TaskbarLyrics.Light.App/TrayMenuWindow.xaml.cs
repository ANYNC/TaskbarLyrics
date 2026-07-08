using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Media = System.Windows.Media;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.Light.App;

public partial class TrayMenuWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int MonitorDpiTypeEffective = 0;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;
    private const byte VkEscape = 0x1B;
    private const int KeyEventKeyUp = 0x0002;

    private readonly Action _toggleLyricsWindow;
    private readonly Action _openSettings;
    private readonly Action _rematchLyrics;
    private readonly Action _clearCaches;
    private readonly Action<AppSettings> _applySettings;
    private readonly Action _exitApp;
    private readonly Func<bool> _isLyricsWindowVisible;
    private readonly Func<AppSettings> _getSettings;
    private readonly DispatcherTimer _outsideClickTimer;
    private TraySubmenuKind? _activeSubmenuKind;
    private bool _wasMouseButtonPressed;
    private DateTime _ignoreOutsideClicksUntilUtc;

    public TrayMenuWindow(
        Action toggleLyricsWindow,
        Action openSettings,
        Action rematchLyrics,
        Action clearCaches,
        Action<AppSettings> applySettings,
        Action exitApp,
        Func<bool> isLyricsWindowVisible,
        Func<AppSettings> getSettings)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        ApplyTheme();
        _toggleLyricsWindow = toggleLyricsWindow;
        _openSettings = openSettings;
        _rematchLyrics = rematchLyrics;
        _clearCaches = clearCaches;
        _applySettings = applySettings;
        _exitApp = exitApp;
        _isLyricsWindowVisible = isLyricsWindowVisible;
        _getSettings = getSettings;
        _outsideClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _outsideClickTimer.Tick += OnOutsideClickTimerTick;
        RefreshStateText();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        SubmenuPopup.CustomPopupPlacementCallback = PlaceSubmenu;
    }

    private void ApplyTheme()
    {
        var light = App.IsSystemUsingLightTheme();
        Resources["TrayMenuBackgroundBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(248, 250, 252)
            : Media.Color.FromRgb(30, 30, 30));
        Resources["TrayMenuHoverBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(229, 234, 242)
            : Media.Color.FromRgb(48, 48, 48));
        Resources["TrayMenuPressedBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(218, 226, 237)
            : Media.Color.FromRgb(58, 58, 58));
        Resources["TrayMenuTextBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(15, 23, 42)
            : Media.Colors.White);
        Resources["TrayMenuSeparatorBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(218, 226, 237)
            : Media.Color.FromRgb(74, 74, 74));
    }

    public void ShowAtCursor(bool animate = true)
    {
        var cursorPhysical = Forms.Cursor.Position;
        var dpi = GetDpiScaleForPoint(cursorPhysical);
        var cursorX = cursorPhysical.X / dpi.X;
        var cursorY = cursorPhysical.Y / dpi.Y;
        var screenPhysical = Forms.Screen.FromPoint(cursorPhysical).WorkingArea;
        var screenLeft = screenPhysical.Left / dpi.X;
        var screenTop = screenPhysical.Top / dpi.Y;
        var screenRight = screenPhysical.Right / dpi.X;
        var screenBottom = screenPhysical.Bottom / dpi.Y;
        const int gap = 8;
        var left = cursorX - Width + 22;
        var top = cursorY - Height - gap;

        if (left < screenLeft + gap)
        {
            left = cursorX - 22;
        }

        if (top < screenTop + gap)
        {
            top = cursorY + gap;
        }

        var minLeft = screenLeft + gap;
        var minTop = screenTop + gap;
        var maxLeft = Math.Max(minLeft, screenRight - Width - gap);
        var maxTop = Math.Max(minTop, screenBottom - Height - gap);
        Left = Math.Clamp(left, minLeft, maxLeft);
        Top = Math.Clamp(top, minTop, maxTop);
        Opacity = 1;
        RefreshStateText();
        if (animate)
        {
            PrepareMenuOpenAnimation();
        }

        if (!IsVisible)
        {
            Show();
        }

        if (animate)
        {
            AnimateMenuOpen();
        }
        else
        {
            MenuChrome.BeginAnimation(OpacityProperty, null);
            MenuTransform.BeginAnimation(Media.TranslateTransform.YProperty, null);
            MenuChrome.Opacity = 1;
            MenuTransform.Y = 0;
        }

        StartOutsideClickDetection();
    }

    public void IgnoreOutsideClicksFor(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        if (until > _ignoreOutsideClicksUntilUtc)
        {
            _ignoreOutsideClicksUntilUtc = until;
        }
    }

    private void StartOutsideClickDetection()
    {
        _wasMouseButtonPressed = IsMouseButtonPressed();
        _outsideClickTimer.Stop();
        _outsideClickTimer.Start();
    }

    private void RefreshStateText()
    {
        var settings = _getSettings();
        LyricsWindowText.Text = _isLyricsWindowVisible()
            ? "隐藏歌词"
            : "显示歌词";
        SpectrumStyleText.Text = settings.EnableSpectrum
            ? $"频谱：{GetSpectrumStyleLabel(settings.SpectrumStyle)}"
            : "频谱：关闭";
        SongProgressStyleText.Text = $"进度：{GetSongProgressStyleLabel(AppSettings.NormalizeSongProgressStyle(settings.SongProgressStyle))}";
        CoverStyleText.Text = settings.ShowCoverImage
            ? $"封面：{GetCoverStyleLabel(settings.CoverStyle)}"
            : "封面：关闭";
        TextEffectStyleText.Text = $"文字：{GetTextEffectStyleLabel(settings.TextEffectStyle)}";
        TransitionStyleText.Text = $"动画：{GetTransitionStyleLabel(AppSettings.NormalizeTransitionStyle(settings.TransitionStyle))}";
        TranslationText.Text = settings.ShowLyricTranslation
            ? "隐藏翻译"
            : "显示翻译";
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(hwnd, GwlExStyle);
        _ = SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _outsideClickTimer.Stop();
        SubmenuPopup.IsOpen = false;
    }

    private void OnOutsideClickTimerTick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            _outsideClickTimer.Stop();
            return;
        }

        var isMouseButtonPressed = IsMouseButtonPressed();
        var isNewClick = isMouseButtonPressed && !_wasMouseButtonPressed;
        _wasMouseButtonPressed = isMouseButtonPressed;
        if (DateTime.UtcNow < _ignoreOutsideClicksUntilUtc)
        {
            return;
        }

        if (!isNewClick)
        {
            return;
        }

        var cursor = Forms.Cursor.Position;
        if (IsPointInsideElement(this, cursor) ||
            (SubmenuPopup.IsOpen && IsPointInsideElement(SubmenuChrome, cursor)))
        {
            return;
        }

        Close();
    }

    private void ToggleLyricsButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        _toggleLyricsWindow();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        _openSettings();
    }

    private void RematchLyricsButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        _rematchLyrics();
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        _clearCaches();
    }

    private void ToggleTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        var snapshot = _getSettings();
        snapshot.ShowLyricTranslation = !snapshot.ShowLyricTranslation;
        _applySettings(snapshot);
    }

    private void SpectrumTuningButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        if (System.Windows.Application.Current is App app)
        {
            app.OpenSpectrumTuningWindow();
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        _exitApp();
    }

    private void CloseSubmenuOnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => CloseSubmenu();

    private void SubmenuPopup_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
    }

    private void SubmenuPopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
    }

    private void SpectrumStyleButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        OpenSubmenu(SpectrumStyleButton, TraySubmenuKind.Spectrum);

    private void SpectrumStyleButton_Click(object sender, RoutedEventArgs e) =>
        OpenSubmenu(SpectrumStyleButton, TraySubmenuKind.Spectrum);

    private void SongProgressStyleButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        OpenSubmenu(SongProgressStyleButton, TraySubmenuKind.SongProgress);

    private void SongProgressStyleButton_Click(object sender, RoutedEventArgs e) =>
        OpenSubmenu(SongProgressStyleButton, TraySubmenuKind.SongProgress);

    private void CoverStyleButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        OpenSubmenu(CoverStyleButton, TraySubmenuKind.Cover);

    private void CoverStyleButton_Click(object sender, RoutedEventArgs e) =>
        OpenSubmenu(CoverStyleButton, TraySubmenuKind.Cover);

    private void TextEffectStyleButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        OpenSubmenu(TextEffectStyleButton, TraySubmenuKind.TextEffect);

    private void TextEffectStyleButton_Click(object sender, RoutedEventArgs e) =>
        OpenSubmenu(TextEffectStyleButton, TraySubmenuKind.TextEffect);

    private void TransitionStyleButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        OpenSubmenu(TransitionStyleButton, TraySubmenuKind.Transition);

    private void TransitionStyleButton_Click(object sender, RoutedEventArgs e) =>
        OpenSubmenu(TransitionStyleButton, TraySubmenuKind.Transition);

    private void OpenSubmenu(System.Windows.Controls.Button placementTarget, TraySubmenuKind kind)
    {
        if (_activeSubmenuKind == kind && SubmenuPopup.IsOpen)
        {
            return;
        }

        _activeSubmenuKind = kind;
        var wasOpen = SubmenuPopup.IsOpen;
        BuildSubmenu(kind);
        SubmenuItemsPanel.UpdateLayout();
        SubmenuPopup.PlacementTarget = placementTarget;
        SubmenuPopup.VerticalOffset = 0;
        SubmenuPopup.HorizontalOffset = wasOpen ? 0.1 : 0;
        SubmenuPopup.IsOpen = true;
        SubmenuPopup.HorizontalOffset = 0;
        if (wasOpen)
        {
            SetSubmenuOpenState();
            return;
        }

        AnimateSubmenuOpen();
    }

    private void CloseSubmenu()
    {
        _activeSubmenuKind = null;
        SubmenuPopup.IsOpen = false;
    }

    private CustomPopupPlacement[] PlaceSubmenu(System.Windows.Size popupSize, System.Windows.Size targetSize, System.Windows.Point offset)
    {
        const double gap = 6;
        return
        [
            new CustomPopupPlacement(new System.Windows.Point(targetSize.Width + gap, 0), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new System.Windows.Point(-popupSize.Width - gap, 0), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new System.Windows.Point(targetSize.Width + gap, targetSize.Height - popupSize.Height), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new System.Windows.Point(-popupSize.Width - gap, targetSize.Height - popupSize.Height), PopupPrimaryAxis.Horizontal)
        ];
    }

    private void BuildSubmenu(TraySubmenuKind kind)
    {
        var settings = _getSettings();
        SubmenuItemsPanel.Children.Clear();
        switch (kind)
        {
            case TraySubmenuKind.Spectrum:
                AddSubmenuOption("关闭", !settings.EnableSpectrum, s => s.EnableSpectrum = false);
                AddSubmenuOption("中心扩散", settings.EnableSpectrum && settings.SpectrumStyle == SpectrumDisplayStyle.Center, s => { s.EnableSpectrum = true; s.SpectrumStyle = SpectrumDisplayStyle.Center; });
                AddSubmenuOption("底部柱状", settings.EnableSpectrum && settings.SpectrumStyle == SpectrumDisplayStyle.Bottom, s => { s.EnableSpectrum = true; s.SpectrumStyle = SpectrumDisplayStyle.Bottom; });
                AddSubmenuOption("镜像波形", settings.EnableSpectrum && settings.SpectrumStyle == SpectrumDisplayStyle.Mirror, s => { s.EnableSpectrum = true; s.SpectrumStyle = SpectrumDisplayStyle.Mirror; });
                AddSubmenuOption("细线", settings.EnableSpectrum && settings.SpectrumStyle == SpectrumDisplayStyle.Thin, s => { s.EnableSpectrum = true; s.SpectrumStyle = SpectrumDisplayStyle.Thin; });
                AddSubmenuOption("点阵", settings.EnableSpectrum && settings.SpectrumStyle == SpectrumDisplayStyle.Dots, s => { s.EnableSpectrum = true; s.SpectrumStyle = SpectrumDisplayStyle.Dots; });
                AddSubmenuOption("呼吸条", settings.EnableSpectrum && settings.SpectrumStyle == SpectrumDisplayStyle.Pulse, s => { s.EnableSpectrum = true; s.SpectrumStyle = SpectrumDisplayStyle.Pulse; });
                break;
            case TraySubmenuKind.SongProgress:
                var songProgressStyle = AppSettings.NormalizeSongProgressStyle(settings.SongProgressStyle);
                AddSubmenuOption("关闭", songProgressStyle == SongProgressDisplayStyle.Off, s => s.SongProgressStyle = SongProgressDisplayStyle.Off);
                AddSubmenuOption("底部细线", songProgressStyle == SongProgressDisplayStyle.BottomLine, s => s.SongProgressStyle = SongProgressDisplayStyle.BottomLine);
                AddSubmenuOption("封面进度环", songProgressStyle == SongProgressDisplayStyle.CoverRing, s => s.SongProgressStyle = SongProgressDisplayStyle.CoverRing);
                AddSubmenuOption("封面底边", songProgressStyle == SongProgressDisplayStyle.CoverBottomBar, s => s.SongProgressStyle = SongProgressDisplayStyle.CoverBottomBar);
                AddSubmenuOption("时间胶囊", songProgressStyle == SongProgressDisplayStyle.TimePill, s => s.SongProgressStyle = SongProgressDisplayStyle.TimePill);
                AddSubmenuOption("呼吸进度点", songProgressStyle == SongProgressDisplayStyle.Dots, s => s.SongProgressStyle = SongProgressDisplayStyle.Dots);
                AddSubmenuOption("边框进度环", songProgressStyle == SongProgressDisplayStyle.BorderRing, s => s.SongProgressStyle = SongProgressDisplayStyle.BorderRing);
                AddSubmenuOption("背景进度", songProgressStyle == SongProgressDisplayStyle.BackgroundFill, s => s.SongProgressStyle = SongProgressDisplayStyle.BackgroundFill);
                break;
            case TraySubmenuKind.Cover:
                AddSubmenuOption("关闭", !settings.ShowCoverImage, s => s.ShowCoverImage = false);
                AddSubmenuOption("隐藏", settings.ShowCoverImage && settings.CoverStyle == CoverDisplayStyle.Hidden, s => { s.ShowCoverImage = true; s.CoverStyle = CoverDisplayStyle.Hidden; });
                AddSubmenuOption("方形", settings.ShowCoverImage && settings.CoverStyle == CoverDisplayStyle.Square, s => { s.ShowCoverImage = true; s.CoverStyle = CoverDisplayStyle.Square; });
                AddSubmenuOption("圆角方形", settings.ShowCoverImage && settings.CoverStyle == CoverDisplayStyle.RoundedSquare, s => { s.ShowCoverImage = true; s.CoverStyle = CoverDisplayStyle.RoundedSquare; });
                AddSubmenuOption("圆形", settings.ShowCoverImage && settings.CoverStyle == CoverDisplayStyle.Circle, s => { s.ShowCoverImage = true; s.CoverStyle = CoverDisplayStyle.Circle; });
                break;
            case TraySubmenuKind.TextEffect:
                AddSubmenuOption("关闭", settings.TextEffectStyle == TextEffectStyle.None, s => s.TextEffectStyle = TextEffectStyle.None);
                AddSubmenuOption("阴影", settings.TextEffectStyle == TextEffectStyle.Shadow, s => s.TextEffectStyle = TextEffectStyle.Shadow);
                AddSubmenuOption("描边", settings.TextEffectStyle == TextEffectStyle.Outline, s => s.TextEffectStyle = TextEffectStyle.Outline);
                AddSubmenuOption("柔光", settings.TextEffectStyle == TextEffectStyle.Glow, s => s.TextEffectStyle = TextEffectStyle.Glow);
                break;
            case TraySubmenuKind.Transition:
                var transitionStyle = AppSettings.NormalizeTransitionStyle(settings.TransitionStyle);
                AddSubmenuOption("关闭", transitionStyle == LyricTransitionStyle.None, s => s.TransitionStyle = LyricTransitionStyle.None);
                AddSubmenuOption("上滑", transitionStyle == LyricTransitionStyle.Slide, s => s.TransitionStyle = LyricTransitionStyle.Slide);
                break;
        }
    }

    private void AddSubmenuOption(string label, bool selected, Action<AppSettings> update)
    {
        var button = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("TraySubmenuButtonStyle"),
            Content = CreateSubmenuOptionContent(label, selected)
        };
        button.Click += (_, _) =>
        {
            var snapshot = _getSettings();
            update(snapshot);
            _applySettings(snapshot);
            RefreshStateText();
            Close();
            DismissTrayOverflow();
        };
        SubmenuItemsPanel.Children.Add(button);
    }

    private Grid CreateSubmenuOptionContent(string label, bool selected)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var check = new TextBlock
        {
            Text = selected ? "✓" : string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Media.Brush)FindResource("TrayMenuTextBrush"),
            FontFamily = new Media.FontFamily("Microsoft YaHei UI"),
            FontSize = 12.5
        };
        Grid.SetColumn(check, 0);
        grid.Children.Add(check);

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Media.Brush)FindResource("TrayMenuTextBrush"),
            FontFamily = new Media.FontFamily("Microsoft YaHei UI"),
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return grid;
    }

    private void AnimateMenuOpen()
    {
        PrepareMenuOpenAnimation();
        var settings = _getSettings();
        var duration = LightMotion.MenuOpenMs(settings.AnimationIntensity);
        var opacityAnimation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = LightMotion.CreateFadeEase(),
            FillBehavior = FillBehavior.Stop
        };
        opacityAnimation.Completed += (_, _) =>
        {
            MenuChrome.BeginAnimation(OpacityProperty, null);
            MenuChrome.Opacity = 1;
        };
        MenuChrome.BeginAnimation(OpacityProperty, opacityAnimation);

        var moveAnimation = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = LightMotion.CreateSoftEase(),
            FillBehavior = FillBehavior.Stop
        };
        moveAnimation.Completed += (_, _) =>
        {
            MenuTransform.BeginAnimation(Media.TranslateTransform.YProperty, null);
            MenuTransform.Y = 0;
        };
        MenuTransform.BeginAnimation(Media.TranslateTransform.YProperty, moveAnimation);
    }

    private void PrepareMenuOpenAnimation()
    {
        MenuChrome.BeginAnimation(OpacityProperty, null);
        MenuTransform.BeginAnimation(Media.TranslateTransform.YProperty, null);
        MenuChrome.Opacity = 0;
        MenuTransform.Y = 6;
    }

    private void AnimateSubmenuOpen()
    {
        var settings = _getSettings();
        var duration = LightMotion.SubmenuOpenMs(settings.AnimationIntensity);
        SubmenuChrome.BeginAnimation(OpacityProperty, null);
        SubmenuTransform.BeginAnimation(Media.TranslateTransform.XProperty, null);
        SubmenuChrome.Opacity = 0;
        SubmenuTransform.X = -5;
        var opacityAnimation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = LightMotion.CreateFadeEase(),
            FillBehavior = FillBehavior.Stop
        };
        opacityAnimation.Completed += (_, _) =>
        {
            SubmenuChrome.BeginAnimation(OpacityProperty, null);
            SubmenuChrome.Opacity = 1;
        };
        SubmenuChrome.BeginAnimation(OpacityProperty, opacityAnimation);

        var moveAnimation = new DoubleAnimation(-5, 0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = LightMotion.CreateSoftEase(),
            FillBehavior = FillBehavior.Stop
        };
        moveAnimation.Completed += (_, _) =>
        {
            SubmenuTransform.BeginAnimation(Media.TranslateTransform.XProperty, null);
            SubmenuTransform.X = 0;
        };
        SubmenuTransform.BeginAnimation(Media.TranslateTransform.XProperty, moveAnimation);
    }

    private void SetSubmenuOpenState()
    {
        SubmenuChrome.BeginAnimation(OpacityProperty, null);
        SubmenuTransform.BeginAnimation(Media.TranslateTransform.XProperty, null);
        SubmenuChrome.Opacity = 1;
        SubmenuTransform.X = 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, int flags, UIntPtr extraInfo);

    private static bool IsMouseButtonPressed()
    {
        return (GetAsyncKeyState(VkLButton) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkRButton) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkMButton) & 0x8000) != 0;
    }

    private static bool IsPointInsideElement(UIElement element, System.Drawing.Point physicalPoint)
    {
        if (!element.IsVisible)
        {
            return false;
        }

        var size = element.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }

        try
        {
            var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = element.PointToScreen(new System.Windows.Point(size.Width, size.Height));
            var left = Math.Min(topLeft.X, bottomRight.X);
            var right = Math.Max(topLeft.X, bottomRight.X);
            var top = Math.Min(topLeft.Y, bottomRight.Y);
            var bottom = Math.Max(topLeft.Y, bottomRight.Y);
            return physicalPoint.X >= left &&
                physicalPoint.X <= right &&
                physicalPoint.Y >= top &&
                physicalPoint.Y <= bottom;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string GetSpectrumStyleLabel(SpectrumDisplayStyle style) => style switch
    {
        SpectrumDisplayStyle.Bottom => "底部柱状",
        SpectrumDisplayStyle.Mirror => "镜像波形",
        SpectrumDisplayStyle.Thin => "细线",
        SpectrumDisplayStyle.Dots => "点阵",
        SpectrumDisplayStyle.Pulse => "呼吸条",
        _ => "中心扩散"
    };

    private static string GetSongProgressStyleLabel(SongProgressDisplayStyle style) => style switch
    {
        SongProgressDisplayStyle.BottomLine => "底部细线",
        SongProgressDisplayStyle.CoverRing => "封面进度环",
        SongProgressDisplayStyle.CoverBottomBar => "封面底边",
        SongProgressDisplayStyle.TimePill => "时间胶囊",
        SongProgressDisplayStyle.Dots => "呼吸进度点",
        SongProgressDisplayStyle.BorderRing => "边框进度环",
        SongProgressDisplayStyle.BackgroundFill => "背景进度",
        _ => "关闭"
    };

    private static string GetCoverStyleLabel(CoverDisplayStyle style) => style switch
    {
        CoverDisplayStyle.Square => "方形",
        CoverDisplayStyle.Circle => "圆形",
        CoverDisplayStyle.Hidden => "隐藏",
        _ => "圆角方形"
    };

    private static string GetTextEffectStyleLabel(TextEffectStyle style) => style switch
    {
        TextEffectStyle.None => "关闭",
        TextEffectStyle.Outline => "描边",
        TextEffectStyle.Glow => "柔光",
        _ => "阴影"
    };

    private static string GetTransitionStyleLabel(LyricTransitionStyle style) => style switch
    {
        LyricTransitionStyle.None => "关闭",
        _ => "上滑"
    };

    private static void DismissTrayOverflow()
    {
        keybd_event(VkEscape, 0, 0, UIntPtr.Zero);
        keybd_event(VkEscape, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static DpiScale GetDpiScaleForPoint(System.Drawing.Point point)
    {
        var monitor = MonitorFromPoint(new NativePoint(point.X, point.Y), MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out var dpiX, out var dpiY) == 0 &&
            dpiX > 0 &&
            dpiY > 0)
        {
            return new DpiScale(dpiX / 96.0, dpiY / 96.0);
        }

        return new DpiScale(1.0, 1.0);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;
    }

    private readonly record struct DpiScale(double X, double Y);

    private enum TraySubmenuKind
    {
        Spectrum,
        SongProgress,
        Cover,
        TextEffect,
        Transition
    }
}
