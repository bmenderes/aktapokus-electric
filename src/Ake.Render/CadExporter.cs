using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using Ake.Core;
using Ake.Core.Model;
using CSMath;

namespace Ake.Render;

public enum CadFormat
{
    Dxf,
    Dwg,
}

/// <summary>
/// Sahneyi DXF ya da DWG dosyasına yazar (ACadSharp, MIT; ADR-0022). Biçim AutoCAD 2013'tür (AC1027):
/// Türkçe harfler ve mm birimi korunur, çoğu CAD programı açar. Her sahne ayrı bir dosyadır.
/// </summary>
public static class CadExporter
{
    /// <summary>Yazı yüksekliği, sahnedeki yazı boyutunun (em) bu katıdır: CAD'de yükseklik büyük harf boyudur.</summary>
    internal const double CapHeightRatio = 0.7;

    internal const string DrawingLayer = "CIZIM";
    internal const string WireLayer = "TEL";
    internal const string TextLayer = "METIN";

    // AutoCAD'in standart çizgi kalınlıkları, 1/100 mm.
    private static readonly short[] LineWeights =
        [0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211];

    public static Result Save(Scene scene, string path, CadFormat format)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(path);

        var document = ToDocument(scene);
        try
        {
            if (format == CadFormat.Dwg)
            {
                DwgWriter.Write(path, document);
            }
            else
            {
                DxfWriter.Write(path, document, false);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("render.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }

    /// <summary>
    /// Sahnenin CAD belgesi. Koordinatlar mm; sahnenin Y'si aşağı, CAD'inki yukarı doğru olduğu için
    /// Y sayfa yüksekliğinden çıkarılır. Görseller (logo) ve bağlantılar CAD'e taşınmaz.
    /// </summary>
    internal static CadDocument ToDocument(Scene scene)
    {
        var document = new CadDocument();
        document.Header.Version = ACadVersion.AC1027;
        document.Header.InsUnits = UnitsType.Millimeters;

        var drawing = new Layer(DrawingLayer) { Color = new Color(7) };
        var wires = new Layer(WireLayer) { Color = new Color(5) };
        var texts = new Layer(TextLayer) { Color = new Color(7) };
        document.Layers.Add(drawing);
        document.Layers.Add(wires);
        document.Layers.Add(texts);

        var style = document.TextStyles[TextStyle.DefaultName];
        style.Filename = "arial.ttf";

        XYZ At(double x, double y) => new(x, scene.HeightMm - y, 0);

        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case LineItem line:
                    document.Entities.Add(new Line
                    {
                        StartPoint = At(line.X1, line.Y1),
                        EndPoint = At(line.X2, line.Y2),
                        Layer = line.Color is null ? drawing : wires,
                        Color = ColorOf(line.Color),
                        LineWeight = WeightOf(line.StrokeMm),
                    });
                    break;

                case RectItem rect:
                    document.Entities.Add(new LwPolyline(
                    [
                        At(rect.X, rect.Y),
                        At(rect.X + rect.Width, rect.Y),
                        At(rect.X + rect.Width, rect.Y + rect.Height),
                        At(rect.X, rect.Y + rect.Height),
                    ])
                    {
                        IsClosed = true,
                        Layer = drawing,
                        Color = new Color(7),
                        LineWeight = WeightOf(rect.StrokeMm),
                    });
                    break;

                case CircleItem { Filled: true } dot:
                    // Dolu nokta: genişliği yarıçap kadar olan, iki yaydan oluşan kapalı çizgi (AutoCAD'in DONUT'u).
                    var half = dot.Radius / 2;
                    document.Entities.Add(new LwPolyline(
                    [
                        new LwPolyline.Vertex(new XY(dot.X - half, scene.HeightMm - dot.Y)) { Bulge = 1, StartWidth = dot.Radius, EndWidth = dot.Radius },
                        new LwPolyline.Vertex(new XY(dot.X + half, scene.HeightMm - dot.Y)) { Bulge = 1, StartWidth = dot.Radius, EndWidth = dot.Radius },
                    ])
                    {
                        IsClosed = true,
                        Layer = drawing,
                        Color = new Color(7),
                    });
                    break;

                case CircleItem circle:
                    document.Entities.Add(new Circle
                    {
                        Center = At(circle.X, circle.Y),
                        Radius = circle.Radius,
                        Layer = drawing,
                        Color = new Color(7),
                        LineWeight = WeightOf(circle.StrokeMm),
                    });
                    break;

                case TextItem text when text.Text.Length > 0:
                    var point = At(text.X, text.Y);
                    document.Entities.Add(new TextEntity
                    {
                        Value = text.Text,
                        InsertPoint = point,
                        AlignmentPoint = point,
                        Height = text.SizeMm * CapHeightRatio,
                        Rotation = -text.RotationDeg * Math.PI / 180,
                        HorizontalAlignment = text.Align switch
                        {
                            TextAlign.Center => TextHorizontalAlignment.Center,
                            TextAlign.Right => TextHorizontalAlignment.Right,
                            _ => TextHorizontalAlignment.Left,
                        },
                        Style = style,
                        Layer = texts,
                        Color = new Color(7),
                    });
                    break;
            }
        }

        return document;
    }

    // Siyah ve renksiz çizgiler 7 numaralı renktir: CAD'in koyu arka planında beyaz, açıkta siyah görünür.
    private static Color ColorOf(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#'
            || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            || rgb == 0)
        {
            return new Color(7);
        }

        return new Color((byte)(rgb >> 16), (byte)((rgb >> 8) & 0xff), (byte)(rgb & 0xff));
    }

    private static LineWeightType WeightOf(double strokeMm)
    {
        var hundredths = strokeMm * 100;
        var nearest = LineWeights.MinBy(weight => Math.Abs(weight - hundredths));
        return (LineWeightType)nearest;
    }
}
