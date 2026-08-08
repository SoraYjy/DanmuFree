using System.Globalization;
using System.Windows.Data;

namespace DanmuFree.App.Converters;

/// 枚举 ↔ bool：用于 RadioButton 双向绑定到枚举属性。
/// ConverterParameter 传枚举名（字符串），匹配则 IsChecked=true；勾选时写回该枚举值。
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null && parameter != null && value.ToString() == parameter.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b && parameter != null
            ? Enum.Parse(targetType, parameter.ToString()!)
            : Binding.DoNothing;
}
