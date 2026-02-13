using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskbarLyrics.Adapters.Netease;
using TaskbarLyrics.Adapters.QQMusic;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Services;
using Media = System.Windows.Media;

namespace TaskbarLyrics.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly IMusicSessionProvider _musicSessionProvider;
    private readonly DispatcherTimer _timer;
    private readonly uint _taskbarCreatedMessage;
    private LyricSyncService _lyricSyncService;
    private string _currentLyric = "TaskbarLyrics started";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _musicSessionProvider = new SmtcMusicSessionProvider();
        _lyricSyncService = BuildLyricSyncService();
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => AnchorToTaskbar();
        IsVisibleChanged += OnIsVisibleChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (System.Windows.Application.Current is App app)
        {
            ApplySettings(app.Settings);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLyric
    {
        get => _currentLyric;
        private set
        {
            if (_currentLyric == value)
            {
                return;
            }

            _currentLyric = value;
            OnPropertyChanged();
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        Width = Math.Clamp(settings.WindowWidth, 260, 1200);
        LyricTextBlock.FontSize = Math.Clamp(settings.FontSize, 10, 40);

        try
        {
            var brush = (Media.Brush?)new Media.BrushConverter().ConvertFromString(settings.ForegroundColor);
            LyricTextBlock.Foreground = brush ?? Media.Brushes.White;
        }
        catch
        {
            LyricTextBlock.Foreground = Media.Brushes.White;
        }

        if (settings.ShowBackground)
        {
            RootBorder.Background = new Media.SolidColorBrush(Media.Color.FromArgb(
                (byte)(Math.Clamp(settings.BackgroundOpacity, 0, 1) * 255),
                17,
                17,
                17));
        }
        else
        {
            RootBorder.Background = Media.Brushes.Transparent;
        }

        if (settings.ShowBorder)
        {
            RootBorder.BorderBrush = new Media.SolidColorBrush(Media.Color.FromArgb(130, 255, 255, 255));
            RootBorder.BorderThickness = new Thickness(1);
        }
        else
        {
            RootBorder.BorderBrush = Media.Brushes.Transparent;
            RootBorder.BorderThickness = new Thickness(0);
        }

        _lyricSyncService = BuildLyricSyncService();
        AnchorToTaskbar();
        AttachToTaskbarHost();
    }

    private LyricSyncService BuildLyricSyncService()
    {
        var providers = new List<ILyricProvider>();

        if (System.Windows.Application.Current is App app)
        {
            if (app.Settings.EnableNetease)
            {
                providers.Add(new NeteaseLyricProvider());
            }

            if (app.Settings.EnableQQMusic)
            {
                providers.Add(new QQMusicLyricProvider());
            }
        }
        else
        {
            providers.Add(new NeteaseLyricProvider());
            providers.Add(new QQMusicLyricProvider());
        }

        return new LyricSyncService(new LyricProviderRegistry(providers));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AnchorToTaskbar();
        AttachToTaskbarHost();
        _timer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            AnchorToTaskbar();
            AttachToTaskbarHost();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App app && !app.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Loaded -= OnLoaded;
        SourceInitialized -= OnSourceInitialized;
        Closing -= OnClosing;
        Closed -= OnClosed;
        IsVisibleChanged -= OnIsVisibleChanged;

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.RemoveHook(WndProc);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AnchorToTaskbar();
            AttachToTaskbarHost();
        });
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var snapshot = await _musicSessionProvider.GetCurrentAsync();
            var line = await _lyricSyncService.GetCurrentLineAsync(snapshot);
            CurrentLyric = string.IsNullOrWhiteSpace(line) ? "Waiting for lyrics..." : line;
        }
        catch (Exception ex)
        {
            CurrentLyric = $"Lyric service error: {ex.Message}";
            Debug.WriteLine(ex);
        }
    }

    private void AnchorToTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var taskbarHeight = Math.Max(32, screenHeight - workArea.Height);

        Height = Math.Max(30, taskbarHeight - 8);

        var settings = (System.Windows.Application.Current as App)?.Settings ?? new AppSettings();
        Left = settings.HorizontalAnchor switch
        {
            LyricsHorizontalAnchor.Left => Math.Max(0, settings.XOffset),
            LyricsHorizontalAnchor.Center => ((screenWidth - Width) / 2.0) + settings.XOffset,
            _ => Math.Max(0, screenWidth - Width - 230 + settings.XOffset)
        };

        Top = screenHeight - taskbarHeight + ((taskbarHeight - Height) / 2.0) + settings.YOffset;
    }

    private void AttachToTaskbarHost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var shellTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (shellTray == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_HWNDPARENT, shellTray);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOP,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_NOOWNERZORDER |
            NativeMethods.SWP_NOSENDCHANGING);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMessage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AnchorToTaskbar();
                AttachToTaskbarHost();
            }));
        }

        return IntPtr.Zero;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class NativeMethods
{
    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
    internal const int GWL_HWNDPARENT = -8;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOSENDCHANGING = 0x0400;
    internal const uint SWP_NOOWNERZORDER = 0x0200;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindowEx(
        IntPtr hWndParent,
        IntPtr hWndChildAfter,
        string? lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
