using System.Text;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace ExpressApp.Auth.Qr;

/// <summary>
/// Генерация QR-кода на стороне клиента.
/// SVG-разметка совпадает с веб-клиентом: пара <see cref="PathString"/> — фон и модули.
/// Матрица модулей отдаётся отдельно, чтобы нарисовать код нативным <c>GraphicsView</c>.
/// </summary>
public interface IQrCodeRenderer
{
    /// <summary>Сторона квадрата в модулях (29 для v3, 57 для v10).</summary>
    int Modules { get; }

    /// <summary>
    /// Кодирует payload в QR и возвращает SVG с viewBox "0 0 N N" плюс матрицу модулей.
    /// </summary>
    QrSvg Render(string payload);
}

/// <param name="Markup">SVG-разметка в формате веб-клиента.</param>
/// <param name="Modules">Сторона сетки в модулях.</param>
/// <param name="Version">Версия QR по ISO/IEC 18004.</param>
/// <param name="FitsTargetGrid">Совпадает ли сетка с требуемыми 57 модулями.</param>
/// <param name="Dark">Плоский массив длиной Modules²: true — тёмный модуль, индекс y*Modules+x.</param>
public readonly record struct QrSvg(
    string Markup,
    int Modules,
    int Version,
    bool FitsTargetGrid,
    bool[] Dark);

public sealed class QrCodeRenderer : IQrCodeRenderer
{
    /// <summary>
    /// Сторона сетки из спецификации — 57 модулей (QR версии 10 по ISO/IEC 18004).
    /// Эталон, а не жёсткое требование: payload с токеном на 64 байта и ключом
    /// не помещается в фиксированную версию, поэтому версия подбирается по данным.
    /// </summary>
    public const int ReferenceModules = 57;

    private const int ReferenceVersion = 10;

    private static readonly Dictionary<EncodeHintType, object> Hints = new()
    {
        { EncodeHintType.MARGIN, 0 },
        { EncodeHintType.CHARACTER_SET, "UTF-8" }
    };

    public int Modules => ReferenceModules;

    public QrSvg Render(string payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload);

        // Спецификация требует сетку 57×57 (QR v10). Сначала пробуем её с более
        // стойким уровнем коррекции M, затем L; если payload не влезает — берём
        // минимально достаточную версию, иначе ZXing бросает
        // "Data too big for requested version".
        var matrix = TryEncode(payload, ErrorCorrectionLevel.M, ReferenceVersion)
                     ?? TryEncode(payload, ErrorCorrectionLevel.L, ReferenceVersion)
                     ?? TryEncode(payload, ErrorCorrectionLevel.L, version: null)
                     ?? throw new InvalidOperationException("Payload не помещается ни в одну версию QR-кода");

        var size = matrix.Width;
        var version = (size - 17) / 4;

        var dark = new bool[size * size];
        var modules = new StringBuilder(payload.Length + size * size / 2);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!matrix[x, y])
                {
                    continue;
                }

                dark[(y * size) + x] = true;
                modules.Append('M').Append(x).Append(' ').Append(y).Append("h1v1h-1z");
            }
        }

        var markup = string.Concat(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" shape-rendering=\"crispEdges\">",
            $"<path fill=\"#FFFFFF\" d=\"M0 0h{size}v{size}h-{size}z\"/>",
            $"<path fill=\"#000000\" d=\"{modules}\"/>",
            "</svg>");

        return new QrSvg(markup, size, version, FitsTargetGrid: size == ReferenceModules, Dark: dark);
    }

    /// <summary>
    /// Кодирует payload в QR выбранной версии; <c>null</c>, если данных больше,
    /// чем вмещает запрошенная версия.
    /// </summary>
    private static BitMatrix? TryEncode(string payload, ErrorCorrectionLevel level, int? version)
    {
        var hints = new Dictionary<EncodeHintType, object>(Hints)
        {
            { EncodeHintType.ERROR_CORRECTION, level }
        };

        if (version is { } forced)
        {
            hints[EncodeHintType.QR_VERSION] = forced;
        }

        try
        {
            return new QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, 0, 0, hints);
        }
        catch (WriterException)
        {
            return null;
        }
    }

    /// <summary>
    /// ASCII-превью матрицы для консоли: удобно сверять сетку 57×57 без GUI.
    /// </summary>
    public static string ToConsolePreview(string payload)
    {
        var qr = new QrCodeRenderer().Render(payload);
        var builder = new StringBuilder(qr.Modules * qr.Modules * 2);

        for (var y = 0; y < qr.Modules; y++)
        {
            for (var x = 0; x < qr.Modules; x++)
            {
                builder.Append(qr.Dark[(y * qr.Modules) + x] ? "██" : "··");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}
