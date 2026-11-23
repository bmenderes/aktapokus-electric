using Ake.Core;
using Ake.Core.Model;
using SkiaSharp;

namespace Ake.Render;

/// <summary>Sahneleri çok sayfalı bir PDF'e yazar (SkiaSharp; ADR-0001).</summary>
public static class PdfExporter
{
    private const float PointsPerMm = 72f / 25.4f;

    /// <param name="baseDirectory">Sahnedeki görsel yollarının göreli olduğu klasör (proje dosyasının klasörü).</param>
    public static Result Save(IReadOnlyList<Scene> scenes, string path, string? baseDirectory, string title)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            Write(scenes, path, baseDirectory, title);
            PdfOutline.Append(
                path,
                [.. scenes.Select((scene, index) => (scene.Bookmark, index))
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Bookmark))
                    .Select(entry => (entry.Bookmark!, entry.index))]);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("render.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }

    private static void Write(IReadOnlyList<Scene> scenes, string path, string? baseDirectory, string title)
    {
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
        {
            Title = title,
            Creator = "aktapokus-electric",
        });

        var typefaces = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var scene in scenes)
            {
                var canvas = document.BeginPage((float)scene.WidthMm * PointsPerMm, (float)scene.HeightMm * PointsPerMm);
                canvas.Scale(PointsPerMm);
                if (scene.Anchor is not null)
                {
                    // Sayfanın sol üstü: içindekiler bağlantılarının hedefi.
                    canvas.DrawNamedDestinationAnnotation(new SKPoint(0, 0), scene.Anchor);
                }

                Draw(canvas, scene, baseDirectory, typefaces);
                document.EndPage();
            }

            document.Close();
        }
        finally
        {
            foreach (var typeface in typefaces.Values)
            {
                typeface.Dispose();
            }
        }
    }

    // Çizgi biçimine göre kesik desen (mm); düz çizgide desen yoktur.
    private static SKPathEffect? DashEffect(Ake.Core.Model.LineStyle style) => style switch
    {
        Ake.Core.Model.LineStyle.Dashed => SKPathEffect.CreateDash([3f, 1.5f], 0),
        Ake.Core.Model.LineStyle.DashDot => SKPathEffect.CreateDash([4f, 1f, 0.8f, 1f], 0),
        Ake.Core.Model.LineStyle.Dotted => SKPathEffect.CreateDash([0.6f, 1f], 0),
        _ => null,
    };

    internal static void Draw(
        SKCanvas canvas, Scene scene, string? baseDirectory, Dictionary<string, SKTypeface> typefaces)
    {
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Black, IsAntialias = true };
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.Black, IsAntialias = true };

        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case LineItem line:
                    stroke.StrokeWidth = (float)line.StrokeMm;
                    stroke.Color = line.Color is not null && SKColor.TryParse(line.Color, out var color) ? color : SKColors.Black;
                    using (var effect = DashEffect(line.Style))
                    {
                        stroke.PathEffect = effect;
                        canvas.DrawLine((float)line.X1, (float)line.Y1, (float)line.X2, (float)line.Y2, stroke);
                        stroke.PathEffect = null;
                    }

                    stroke.Color = SKColors.Black;
                    break;

                case CircleItem circle:
                    stroke.StrokeWidth = (float)circle.StrokeMm;
                    canvas.DrawCircle((float)circle.X, (float)circle.Y, (float)circle.Radius, circle.Filled ? fill : stroke);
                    break;

                case RectItem rect:
                    stroke.StrokeWidth = (float)rect.StrokeMm;
                    canvas.DrawRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, stroke);
                    break;

                case TextItem text:
                    if (!typefaces.TryGetValue(text.FontFamily, out var typeface))
                    {
                        typeface = SKTypeface.FromFamilyName(text.FontFamily) ?? SKTypeface.Default;
                        typefaces[text.FontFamily] = typeface;
                    }

                    using (var textFill = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true,
                        Color = text.Color is not null && SKColor.TryParse(text.Color, out var textColor) ? textColor : SKColors.Black,
                    })
                    using (var font = new SKFont(typeface, (float)text.SizeMm))
                    {
                        var align = text.Align switch
                        {
                            TextAlign.Center => SKTextAlign.Center,
                            TextAlign.Right => SKTextAlign.Right,
                            _ => SKTextAlign.Left,
                        };
                        if (text.RotationDeg == 0)
                        {
                            canvas.DrawText(text.Text, (float)text.X, (float)text.Y, align, font, textFill);
                        }
                        else
                        {
                            canvas.Save();
                            canvas.RotateDegrees((float)text.RotationDeg, (float)text.X, (float)text.Y);
                            canvas.DrawText(text.Text, (float)text.X, (float)text.Y, align, font, textFill);
                            canvas.Restore();
                        }
                    }

                    break;

                case ImageItem image:
                    DrawImage(canvas, image, baseDirectory);
                    break;

                case LinkItem link:
                    canvas.DrawLinkDestinationAnnotation(
                        new SKRect((float)link.X, (float)link.Y, (float)(link.X + link.Width), (float)(link.Y + link.Height)),
                        link.Target);
                    break;
            }
        }
    }

    private const long MaxImageBytes = 20L * 1024 * 1024;
    private static readonly string[] AllowedImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif", ".ico"];

    private static bool IsUnsafeLocalImagePath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal)
        || (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsUnc);

    // Görsel bulunamazsa, güvensiz UNC/ağ yoluysa ya da okunamazsa sessizce atlanır: eksik logo çıktıyı engellememeli.
    private static void DrawImage(SKCanvas canvas, ImageItem item, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(item.Path) || IsUnsafeLocalImagePath(item.Path))
        {
            return;
        }

        string path;
        try
        {
            var combined = baseDirectory is null || Path.IsPathRooted(item.Path)
                ? item.Path
                : Path.Combine(baseDirectory, item.Path);
            if (IsUnsafeLocalImagePath(combined))
            {
                return;
            }

            path = Path.GetFullPath(combined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        var ext = Path.GetExtension(path);
        if (IsUnsafeLocalImagePath(path)
            || !AllowedImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            return;
        }

        try
        {
            var len = new FileInfo(path).Length;
            if (len <= 0 || len > MaxImageBytes)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        using var image = SKImage.FromEncodedData(path);
        if (image is null || image.Width == 0 || image.Height == 0)
        {
            return;
        }

        var scale = Math.Min(item.Width / image.Width, item.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var x = item.X + ((item.Width - width) / 2);
        var y = item.Y + ((item.Height - height) / 2);
        canvas.DrawImage(
            image,
            new SKRect((float)x, (float)y, (float)(x + width), (float)(y + height)),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }
}
