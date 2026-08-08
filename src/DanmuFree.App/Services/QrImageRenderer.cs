using QRCoder;
using System.IO;
using System.Windows.Media.Imaging;

namespace DanmuFree.App.Services;

public static class QrImageRenderer
{
    public static BitmapImage Render(string text)
    {
        using var gen = new QRCodeGenerator();
        var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(20);
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = new MemoryStream(png);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
