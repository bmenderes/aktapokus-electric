using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using CSMath;

namespace Ake.Library;

/// <summary>DXF/DWG çiziminden sayfa olarak okunan serbest grafik (ADR-0057).</summary>
public sealed record DxfPageItem(
    int Number,
    string Title,
    double WidthMm,
    double HeightMm,
    IReadOnlyList<GraphicShape> Shapes);

/// <summary>Bir veya birden çok DXF/DWG dosyasından okunan sayfalar.</summary>
public sealed record DxfPageDrawing(IReadOnlyList<DxfPageItem> Pages);

/// <summary>
/// SEE Electrical veya diğer CAD araçlarının dışa aktardığı tam DXF/DWG çizimlerini sayfaya
/// düzenlenebilir serbest grafik (<see cref="PageGraphic"/>) olarak aktarır (ADR-0028, ADR-0057).
/// </summary>
public static class DxfPageImport
{
    private const int CurveSegments = 24;
    private const int MaxDepth = 16;

    /// <summary>Tek bir DXF/DWG/.dxf.zip dosyasını ya da klasördeki tüm çizimleri sayfa sayfa okur.</summary>
    public static Result<DxfPageDrawing> Read(string path, bool keepFrame = false)
    {
        ArgumentNullException.ThrowIfNull(path);

        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(DxfImport.IsCadFile)
                .Order(StringComparer.Ordinal)
                .ToList()
            : File.Exists(path) ? [path] : [];

        if (files.Count == 0)
        {
            return Result.Failure<DxfPageDrawing>(new Error("dxf.yol_yok", $"DXF/DWG dosyası bulunamadı: '{path}'"));
        }

        var pages = new List<DxfPageItem>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var opened = DxfImport.ReadDocument(files[i]);
            if (opened.IsFailure)
            {
                return Result.Failure<DxfPageDrawing>(opened.Error!);
            }

            var doc = opened.Value.Document;
            var k = UnitsToMm(doc.Header.InsUnits);
            var collector = new Collector();
            var model = doc.BlockRecords[BlockRecord.ModelSpaceName];
            foreach (var entity in model.Entities)
            {
                collector.Visit(entity, 0);
            }

            if (collector.IsEmpty)
            {
                continue;
            }

            var (minX, minY, maxX, maxY) = collector.Bounds();
            var widthMm = Math.Max(10, Math.Round((maxX - minX) * k, 2));
            var heightMm = Math.Max(10, Math.Round((maxY - minY) * k, 2));

            if (!keepFrame)
            {
                collector.DropOuterFrame(minX, minY, maxX, maxY);
                if (!collector.IsEmpty)
                {
                    (minX, minY, maxX, maxY) = collector.Bounds();
                    widthMm = Math.Max(10, Math.Round((maxX - minX) * k, 2));
                    heightMm = Math.Max(10, Math.Round((maxY - minY) * k, 2));
                }
            }

            double X(double x) => Math.Round((x - minX) * k, 3);
            double Y(double y) => Math.Round((maxY - y) * k, 3);

            var shapes = new List<GraphicShape>();
            foreach (var (x1, y1, x2, y2, color) in collector.Segments)
            {
                var px1 = X(x1);
                var py1 = Y(y1);
                var px2 = X(x2);
                var py2 = Y(y2);
                if (Math.Abs(px1 - px2) < 0.001 && Math.Abs(py1 - py2) < 0.001)
                {
                    continue;
                }

                shapes.Add(new GraphicPolyline([px1, py1, px2, py2], Closed: false, StrokeMm: 0.25) { Color = color });
            }

            foreach (var (cx, cy, r, color) in collector.Circles)
            {
                var radius = Math.Round(r * k, 3);
                if (radius > 0)
                {
                    shapes.Add(new GraphicCircle(X(cx), Y(cy), radius, StrokeMm: 0.25) { Color = color });
                }
            }

            foreach (var (tx, ty, text, h, align, rot, color) in collector.Texts)
            {
                var size = Math.Clamp(Math.Round(h * k, 2), 1.0, 20.0);
                shapes.Add(new GraphicText(X(tx), Y(ty), text, size, align, rot) { Color = color });
            }

            var title = Path.GetFileNameWithoutExtension(files[i]);
            if (title.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
            {
                title = Path.GetFileNameWithoutExtension(title);
            }

            pages.Add(new DxfPageItem(pages.Count + 1, title, widthMm, heightMm, shapes));
        }

        if (pages.Count == 0)
        {
            return Result.Failure<DxfPageDrawing>(new Error("dxf.bos", "DXF/DWG çiziminde alınabilecek öğe yok."));
        }

        return Result.Success(new DxfPageDrawing(pages));
    }

    /// <summary>Okunan DXF sayfalarını projenin sonuna tek geri alınabilir işlem olarak ekler.</summary>
    public static Result<Transaction> Plan(
        Project project,
        DxfPageDrawing drawing,
        (double X, double Y, double Width, double Height)? targetArea = null,
        SheetTemplateId? template = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(drawing);

        if (drawing.Pages.Count == 0)
        {
            return Result.Failure<Transaction>(new Error("dxf.bos", "DXF çiziminde sayfa yok."));
        }

        var templateId = template ?? project.SheetTemplates.FirstOrDefault()?.Id;
        if (templateId is null)
        {
            return Result.Failure<Transaction>(new Error("dxf.sablon_yok", "Projede sayfa şablonu yok; önce bir şablon ekleyin."));
        }

        var commands = new List<ICommand>();
        var used = project.Pages.Select(p => p.Number).ToHashSet(StringComparer.Ordinal);
        var nextNum = project.Pages
            .Select(p => int.TryParse(p.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        foreach (var source in drawing.Pages)
        {
            while (used.Contains(nextNum.ToString(CultureInfo.InvariantCulture)))
            {
                nextNum++;
            }

            var number = nextNum.ToString(CultureInfo.InvariantCulture);
            used.Add(number);
            var page = new Page(PageId.New(), number, source.Title, templateId: templateId);
            commands.Add(new AddPage(page));

            var scale = targetArea is { } area
                ? Math.Min(1.0, Math.Min((area.Width - 4) / source.WidthMm, (area.Height - 4) / source.HeightMm))
                : 1.0;
            var offsetX = targetArea?.X ?? 15.0;
            var offsetY = targetArea?.Y ?? 15.0;

            if (source.Shapes.Count > 0)
            {
                commands.Add(new AddGraphics(source.Shapes.Select(shape =>
                    new PageGraphic(GraphicId.New(), page.Id, TransformShape(shape, scale, offsetX, offsetY)))));
            }
        }

        return Result.Success(new Transaction($"DXF içe aktarıldı: {drawing.Pages.Count} sayfa", commands));
    }

    private static GraphicShape TransformShape(GraphicShape shape, double scale, double dx, double dy) => shape switch
    {
        GraphicPolyline p => p with
        {
            Points = [.. p.Points.Select((v, idx) => Math.Round((v * scale) + (idx % 2 == 0 ? dx : dy), 3))],
        },
        GraphicCircle c => c with
        {
            X = Math.Round((c.X * scale) + dx, 3),
            Y = Math.Round((c.Y * scale) + dy, 3),
            Radius = Math.Max(0.1, Math.Round(c.Radius * scale, 3)),
        },
        GraphicText t => t with
        {
            X = Math.Round((t.X * scale) + dx, 3),
            Y = Math.Round((t.Y * scale) + dy, 3),
            SizeMm = Math.Max(1.0, Math.Round(t.SizeMm * scale, 2)),
        },
        _ => shape.Translate(dx, dy),
    };

    private static double UnitsToMm(UnitsType units) => units switch
    {
        UnitsType.Inches => 25.4,
        UnitsType.Feet => 304.8,
        UnitsType.Yards => 914.4,
        UnitsType.Mils => 0.0254,
        UnitsType.Microns => 0.001,
        UnitsType.Centimeters => 10,
        UnitsType.Decimeters => 100,
        UnitsType.Meters => 1000,
        _ => 1,
    };

    private sealed class Collector
    {
        public List<(double X1, double Y1, double X2, double Y2, string? Color)> Segments { get; } = [];
        public List<(double X, double Y, double Radius, string? Color)> Circles { get; } = [];
        public List<(double X, double Y, string Text, double Height, TextAlign Align, double RotationDeg, string? Color)> Texts { get; } = [];

        public bool IsEmpty => Segments.Count == 0 && Circles.Count == 0 && Texts.Count == 0;

        public void Visit(Entity entity, int depth, short parentColor = 7)
        {
            if (depth > MaxDepth || entity.IsInvisible || (entity.Layer is { } layer && (!layer.IsOn || layer.Flags.HasFlag(LayerFlags.Frozen))))
            {
                return;
            }

            var aci = ResolveColorIndex(entity, parentColor);
            var hex = AciToHex(aci);

            switch (entity)
            {
                case Line line:
                    Segments.Add((line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y, hex));
                    break;

                case Arc arc:
                    var steps = Math.Max(2, (int)Math.Ceiling(CurveSegments * Math.Abs(arc.Sweep) / (2 * Math.PI)));
                    Chain(Ocs(arc.PolygonalVertexes(steps + 1), arc.Normal), closed: false, hex);
                    break;

                case Circle circle:
                    Circles.Add((circle.Normal.Z < 0 ? -circle.Center.X : circle.Center.X, circle.Center.Y, circle.Radius, hex));
                    break;

                case Ellipse ellipse:
                    Chain([.. ellipse.PolygonalVertexes((CurveSegments * 2) + 1).Select(p => (p.X, p.Y))], closed: false, hex);
                    break;

                case LwPolyline polyline:
                    Polyline([.. polyline.Vertices.Select(v => (v.Location.X, v.Location.Y, v.Bulge))], polyline.IsClosed, polyline.Normal, hex);
                    break;

                case IPolyline polyline:
                    Polyline([.. polyline.Vertices.Select(v => (Coord(v.Location, 0), Coord(v.Location, 1), v.Bulge))], polyline.IsClosed, XYZ.AxisZ, hex);
                    break;

                case Spline spline:
                    if (spline.TryPolygonalVertexes(CurveSegments * 2, out var vertices) && vertices.Count > 1)
                    {
                        Chain([.. vertices.Select(p => (p.X, p.Y))], closed: false, hex);
                    }
                    else
                    {
                        Chain([.. spline.ControlPoints.Select(p => (p.X, p.Y))], closed: false, hex);
                    }

                    break;

                case Solid solid:
                    Chain(
                        [
                            (solid.FirstCorner.X, solid.FirstCorner.Y), (solid.SecondCorner.X, solid.SecondCorner.Y),
                            (solid.FourthCorner.X, solid.FourthCorner.Y), (solid.ThirdCorner.X, solid.ThirdCorner.Y),
                        ],
                        closed: true,
                        hex);
                    break;

                case TextEntity text:
                    var align = text.HorizontalAlignment switch
                    {
                        TextHorizontalAlignment.Center or TextHorizontalAlignment.Middle => TextAlign.Center,
                        TextHorizontalAlignment.Right => TextAlign.Right,
                        _ => TextAlign.Left,
                    };
                    var at = align == TextAlign.Left ? text.InsertPoint : text.AlignmentPoint;
                    AddText(at.X, at.Y, text.Value, text.Height, align, -Math.Round(text.Rotation * 180 / Math.PI, 1), hex);
                    break;

                case MText mtext:
                    var lines = mtext.GetPlainTextLines().Where(l => l.Trim().Length > 0).ToList();
                    for (var i = 0; i < lines.Count; i++)
                    {
                        AddText(mtext.InsertPoint.X, mtext.InsertPoint.Y - (i * mtext.Height * 1.4), lines[i], mtext.Height, TextAlign.Left, -Math.Round(mtext.Rotation * 180 / Math.PI, 1), hex);
                    }

                    break;

                case Insert insert:
                    foreach (var child in insert.Explode())
                    {
                        Visit(child, depth + 1, aci);
                    }

                    break;

                case Dimension dimension when dimension.Block is { } block:
                    foreach (var child in block.Entities)
                    {
                        Visit(child, depth + 1, aci);
                    }

                    break;
            }
        }

        public void DropOuterFrame(double minX, double minY, double maxX, double maxY)
        {
            var w = maxX - minX;
            var h = maxY - minY;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            Segments.RemoveAll(s =>
            {
                var horizontal = Math.Abs(s.Y1 - s.Y2) < 0.5 && Math.Abs(s.X2 - s.X1) > w * 0.85
                    && (Math.Abs(s.Y1 - minY) < 1 || Math.Abs(s.Y1 - maxY) < 1);
                var vertical = Math.Abs(s.X1 - s.X2) < 0.5 && Math.Abs(s.Y2 - s.Y1) > h * 0.85
                    && (Math.Abs(s.X1 - minX) < 1 || Math.Abs(s.X1 - maxX) < 1);
                return horizontal || vertical;
            });
        }

        public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
        {
            var xs = Segments.SelectMany(s => new[] { s.X1, s.X2 })
                .Concat(Circles.SelectMany(c => new[] { c.X - c.Radius, c.X + c.Radius }))
                .Concat(Texts.Select(t => t.X))
                .ToList();
            var ys = Segments.SelectMany(s => new[] { s.Y1, s.Y2 })
                .Concat(Circles.SelectMany(c => new[] { c.Y - c.Radius, c.Y + c.Radius }))
                .Concat(Texts.Select(t => t.Y))
                .ToList();
            return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }

        private void AddText(double x, double y, string value, double height, TextAlign align, double rotDeg, string? color)
        {
            var clean = value
                .Replace("%%c", "Ø", StringComparison.OrdinalIgnoreCase)
                .Replace("%%d", "°", StringComparison.OrdinalIgnoreCase)
                .Replace("%%p", "±", StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (clean.Length > 0 && height > 0)
            {
                Texts.Add((x, y, clean, height, align, Math.Abs(rotDeg) < 0.5 ? 0 : rotDeg, color));
            }
        }

        private void Polyline(List<(double X, double Y, double Bulge)> vertices, bool closed, XYZ normal, string? color)
        {
            if (vertices.Count < 2)
            {
                return;
            }

            var count = closed ? vertices.Count : vertices.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var (a, b) = (vertices[i], vertices[(i + 1) % vertices.Count]);
                if (Math.Abs(a.Bulge) < 1e-9)
                {
                    Chain(Ocs([new XYZ(a.X, a.Y, 0), new XYZ(b.X, b.Y, 0)], normal), closed: false, color);
                    continue;
                }

                var sweep = 4 * Math.Atan(Math.Abs(a.Bulge));
                var steps = Math.Max(2, (int)Math.Ceiling(CurveSegments * sweep / (2 * Math.PI)));
                var arc = Arc.CreateFromBulge(new XY(a.X, a.Y), new XY(b.X, b.Y), a.Bulge);
                Chain(Ocs(arc.PolygonalVertexes(steps + 1), normal), closed: false, color);
            }
        }

        private void Chain(List<(double X, double Y)> points, bool closed, string? color)
        {
            for (var i = 1; i < points.Count; i++)
            {
                Segments.Add((points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, color));
            }

            if (closed && points.Count > 2)
            {
                Segments.Add((points[^1].X, points[^1].Y, points[0].X, points[0].Y, color));
            }
        }

        private static double Coord(IVector v, int idx) => v switch
        {
            XY xy => idx == 0 ? xy.X : xy.Y,
            XYZ xyz => idx == 0 ? xyz.X : xyz.Y,
            _ => 0,
        };

        private static List<(double X, double Y)> Ocs(IEnumerable<XYZ> points, XYZ normal) =>
            [.. points.Select(p => normal.Z < 0 ? (-p.X, p.Y) : (p.X, p.Y))];

        private static short ResolveColorIndex(Entity entity, short parentColor)
        {
            if (entity.Color.IsByBlock)
            {
                return parentColor;
            }

            if (entity.Color.IsByLayer && entity.Layer is not null)
            {
                return entity.Layer.Color.Index;
            }

            return entity.Color.Index;
        }

        private static string? AciToHex(short index) => index switch
        {
            1 => "#CC0000",
            2 => "#B38600",
            3 => "#008000",
            4 => "#008080",
            5 => "#0044CC",
            6 => "#990099",
            30 or 40 => "#E65100",
            8 or 9 or 250 or 251 or 252 or 253 => "#666666",
            _ => null,
        };
    }
}
