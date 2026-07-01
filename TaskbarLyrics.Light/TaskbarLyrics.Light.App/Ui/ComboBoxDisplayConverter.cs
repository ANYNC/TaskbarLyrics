using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace TaskbarLyrics.Light.App.Ui;

/// <summary>
/// 从 ComboBoxItem 或字符串提取下拉框当前显示文本。
/// </summary>
public sealed class ComboBoxDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ComboBoxItem item)
        {
            return item.Content?.ToString() ?? string.Empty;
        }

        if (value is SettingsWindow.FontOption option)
        {
            return option.Label;
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
