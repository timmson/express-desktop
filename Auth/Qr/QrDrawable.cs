using Microsoft.Maui.Graphics;

namespace ExpressApp.Auth.Qr;

/// <summary>
/// Отрисовка QR-кода нативным <see cref="GraphicsView"/>: квадрат со светлой
/// рамкой (quiet zone) и тёмными модулями из <see cref="QrSvg.Dark"/>.
/// </summary>
public sealed class QrDrawable : IDrawable
{
    /// <summary>Quiet zone по ISO/IEC 18004 — 4 модуля.</summary>
    private const int QuietZoneModules = 4;

    private QrSvg? _qr;

    public void Set(QrSvg? qr) => _qr = qr;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (_qr is not { } qr)
        {
            return;
        }

        var side = Math.Min(dirtyRect.Width, dirtyRect.Height);
        if (side <= 0)
        {
            return;
        }

        var originX = dirtyRect.X + (dirtyRect.Width - side) / 2f;
        var originY = dirtyRect.Y + (dirtyRect.Height - side) / 2f;

        canvas.FillColor = Colors.White;
        canvas.FillRectangle(originX, originY, side, side);

        // Сетка занимает сторону минус quiet zone с двух сторон.
        var gridSide = side * (qr.Modules / (float)(qr.Modules + 2 * QuietZoneModules));
        var offset = (side - gridSide) / 2f;
        var module = gridSide / qr.Modules;

        canvas.FillColor = Colors.Black;
        for (var y = 0; y < qr.Modules; y++)
        {
            for (var x = 0; x < qr.Modules; x++)
            {
                if (!qr.Dark[(y * qr.Modules) + x])
                {
                    continue;
                }

                // +0.5f перекрывает субпиксельные щели между соседними модулями.
                canvas.FillRectangle(
                    originX + offset + (x * module),
                    originY + offset + (y * module),
                    module + 0.5f,
                    module + 0.5f);
            }
        }
    }
}
