using System.Globalization;
using System.Windows.Data;

namespace DanmuFree.App.Converters;

/// <summary>
/// 多值转换：values[0] = 消息时间 (DateTime)，values[1] = 是否显示时间 (bool)。
/// 显示则返回 "HH:mm:ss "，否则返回空串（Run 无 Visibility 属性，用空文本实现隐藏）。
/// </summary>
public sealed class TimeIfShownConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not DateTime time) return "";
        return values[1] is true ? time.ToString("HH:mm:ss ", culture) : "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
