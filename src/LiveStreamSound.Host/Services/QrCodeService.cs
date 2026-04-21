using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;
using LiveStreamSound.Shared.Discovery;

namespace LiveStreamSound.Host.Services;

public static class QrCodeService
{
    public static string BuildConnectionUri(string hostIp, int controlPort, string code) =>
        $"{DiscoveryConstants.UriScheme}://{hostIp}:{controlPort}?code={code}";

    public static BitmapSource GeneratePng(string text, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var pngQr = new PngByteQRCode(data);
        var pngBytes = pngQr.GetGraphic(pixelsPerModule);

        var bitmap = new BitmapImage();
        using var ms = new MemoryStream(pngBytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
