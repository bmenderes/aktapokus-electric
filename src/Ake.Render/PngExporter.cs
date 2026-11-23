using Ake.Core;
using SkiaSharp;

namespace Ake.Render;

/// <summary>Tek sayfayı PNG görüntüsüne çevirir (önizleme ve gözle doğrulama için).</summary>
public static class PngExporter
{
    public static Result Save(Scene scene, string path, string? baseDirectory, int dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);

        var pixelsPerMm = dpi / 25.4f;
        var info = new SKImageInfo(
            (int)Math.Ceiling(scene.WidthMm * pixelsPerMm), (int)Math.Ceiling(scene.HeightMm * pixelsPerMm));

        try
        {
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            canvas.Scale(pixelsPerMm);

            var typefaces = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);
            try
            {
                PdfExporter.Draw(canvas, scene, baseDirectory, typefaces);
            }
            finally
            {
                foreach (var typeface in typefaces.Values)
                {
                    typeface.Dispose();
                }
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(path);
            data.SaveTo(stream);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("render.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }
}
