using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace DanmuFree.App.Converters;

/// 字符串相等判定：一器两用。
/// ① RadioButton.IsChecked（targetType=bool，双向）：属性值 == ConverterParameter → true；
///   勾选写回 ConverterParameter，取消勾选 Binding.DoNothing（让同组另一个去写）。
/// ② 面板 Visibility（targetType=Visibility，单向）：相等 → Visible，否则 Collapsed。
/// 用于「朗读」TAB 按 TtsEngine 切换 GPT-SoVITS / 系统内置 两组控件。
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var eq = value != null && parameter != null && value.ToString() == parameter.ToString();
        return targetType == typeof(Visibility)
            ? (eq ? Visibility.Visible : Visibility.Collapsed)
            : (object)eq;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b && parameter != null ? parameter.ToString()! : Binding.DoNothing;
}
