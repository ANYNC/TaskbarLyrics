using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarLyrics.Light.App.Ui;

public partial class SourceOrderList : System.Windows.Controls.UserControl
{
    private static readonly Dictionary<string, string> SourceNames = new()
    {
        ["QQMusic"] = "QQ音乐",
        ["Netease"] = "网易云音乐",
        ["Kugou"] = "酷狗音乐",
        ["Spotify"] = "Spotify"
    };

    private static readonly Dictionary<string, string> SourceIcons = new()
    {
        ["QQMusic"] = "QQ音乐.png",
        ["Netease"] = "网易云音乐.png",
        ["Kugou"] = "酷狗音乐.png",
        ["Spotify"] = "spotify.png"
    };

    private System.Windows.Point _dragStart;
    private string? _draggedSource;

    public event EventHandler? OrderChanged;

    public SourceOrderList()
    {
        InitializeComponent();
    }

    public void SetOrder(IEnumerable<string> order)
    {
        OrderList.ItemsSource = order
            .Select(source => new SourceOrderItem(source, GetDisplayName(source), LoadIcon(source)))
            .ToList();
    }

    public List<string> GetOrder() =>
        OrderList.Items.Cast<SourceOrderItem>().Select(x => x.Source).ToList();

    private void OrderList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        if (ItemsControl.ContainerFromElement(OrderList, e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is SourceOrderItem entry)
        {
            _draggedSource = entry.Source;
        }
    }

    private void OrderList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || string.IsNullOrEmpty(_draggedSource))
        {
            return;
        }

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(OrderList, _draggedSource, System.Windows.DragDropEffects.Move);
    }

    private void OrderList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(string)) || e.Data.GetData(typeof(string)) is not string source)
        {
            return;
        }

        var targetItem = ItemsControl.ContainerFromElement(OrderList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (targetItem?.DataContext is not SourceOrderItem targetEntry || targetEntry.Source == source)
        {
            return;
        }

        var items = GetOrder();
        var from = items.IndexOf(source);
        var to = items.IndexOf(targetEntry.Source);
        if (from < 0 || to < 0)
        {
            return;
        }

        items.RemoveAt(from);
        items.Insert(to, source);
        SetOrder(items);
        OrderChanged?.Invoke(this, EventArgs.Empty);
        _draggedSource = null;
    }

    private static string GetDisplayName(string source) =>
        SourceNames.TryGetValue(source, out var name) ? name : source;

    private static ImageSource? LoadIcon(string source)
    {
        if (!SourceIcons.TryGetValue(source, out var file))
        {
            return null;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "PlayerIcons", file);
        if (!File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public sealed class SourceOrderItem(string source, string displayName, ImageSource? icon)
    {
        public string Source { get; } = source;
        public string DisplayName { get; } = displayName;
        public ImageSource? Icon { get; } = icon;
    }
}
