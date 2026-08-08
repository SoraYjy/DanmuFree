using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DanmuFree.Core.Models;
namespace DanmuFree.App.Converters;

public sealed class MessageTypeToBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush Danmu = new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    public static readonly SolidColorBrush Gift = new(Color.FromRgb(0xFF, 0xC1, 0x07));
    public static readonly SolidColorBrush SuperChat = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    public static readonly SolidColorBrush Interact = new(Color.FromRgb(0x9E, 0x9E, 0x9E));
    public static readonly SolidColorBrush OnlineCount = new(Color.FromRgb(0x4F, 0xC3, 0xF7));

    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is MessageType mt ? mt switch
    {
        MessageType.Danmu => Danmu,
        MessageType.Gift => Gift,
        MessageType.SuperChat => SuperChat,
        MessageType.Interact => Interact,
        _ => OnlineCount,
    } : Danmu;

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}
