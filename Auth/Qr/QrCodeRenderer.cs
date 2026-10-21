using System.Globalization;
using System.Text;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace ExpressApp.Auth.Qr;

/// <summary>
/// Генерация QR-кода на стороне клиента в виде SVG.
/// Формат совпадает с веб-клиентом: пара <see cref="PathString"/> — фон и модули.
/// </summary>
public interface IQrCodeRenderer
{
    /// <summary>Сторона квадрата в модулях (29 для v3, 57 для v10).</summary>
    int Modules { get; }

    /// <summary>
    /// Кодирует payload в QR и возвращает SVG с viewBox "0 0 N N".
    /// </summary>
    QrSvg Render(string payload);
}

public readonly record struct QrSvg(string Markup, int Modules, int Version, bool FitsTargetGrid);

public sealed class QrCodeRenderer : IQrCodeRenderer
{
    /// <summary>
    /// Требуемая заказчиком сторона сетки — 57 модулей.
    /// По стандарту ISO/IEC 18004 это QR версии 10 (21 + 4·10); в спецификации
    /// указана версия 3, что даёт 29 модулей — несоответствие отмечено в README.
    /// </summary>
    public const int TargetModules = 57;

    private const int QrVersionFor57Modules = 10;

    private static readonly Dictionary<EncodeHintType, object> Hints = new()
    {
        { EncodeHintType.MARGIN, 0 },
        { EncodeHintType.CHARACTER_SET, "UTF-8" },
        { EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.M },
        { EncodeHintType.QR_VERSION, QrVersionFor57Modules }
    };

    public int Modules => TargetModules;

    public QrSvg Render(string payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload);

        var matrix = new QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, 0, 0, Hints);
        var size = matrix.Width;

        var modules = new StringBuilder(payload.Length + size * size / 2);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (matrix[x, y])
                {
                    modules.Append('M').Append(x).Append(' ').Append(y).Append("h1v1h-1z");
                }
            }
        }

        var markup = string.Concat(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" shape-rendering=\"crispEdges\">",
            $"<path fill=\"#FFFFFF\" d=\"M0 0h{size}v{size}h-{size}z\"/>",
            $"<path fill=\"#000000\" d=\"{modules}\"/>",
            "</svg>");

        return new QrSvg(
            markup,
            size,
            QrVersionFor57Modules,
            FitsTargetGrid: size == TargetModules);
    }

    /// <summary>
    /// ASCII-превью матрицы для консоли: удобно сверять сетку 57×57 без GUI.
    /// </summary>
    public static string ToConsolePreview(string payload)
    {
        var matrix = new QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, 0, 0, Hints);
        var builder = new StringBuilder(matrix.Width * matrix.Height * 2);
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                builder.Append(matrix[x, y] ? "██" : "··");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}
