using System.Globalization;
using Ake.Render;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TextAlign = Ake.Core.Model.TextAlign;

namespace Ake.Editor;

/// <summary>
/// Sahneyi ekrana çizer: tekerlekle yakınlaştırma, boş alanda sürükleyerek kaydırma, sembol üzerinde
/// sürükleyerek taşıma. PDF ve SVG ile aynı sahneyi kullanır; ekranda görünen, çıktıda çıkandır.
/// Ne yapılacağına karar vermez; basılan noktayı ve sürükleme mesafesini (mm) bildirir.
/// </summary>
public sealed class SceneView : Control
{
    private static readonly IBrush Backdrop = new SolidColorBrush(Color.FromRgb(0xd9, 0xd2, 0xbf));
    private static readonly IBrush DarkBackdrop = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1D));
    private static readonly IBrush DarkPaper = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x2A));
    private static readonly IBrush DarkFineGrid = new SolidColorBrush(Color.FromRgb(0x2D, 0x33, 0x40));
    private static readonly IBrush DarkCoarseGrid = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x68));
    private static readonly IBrush DarkInk = new SolidColorBrush(Color.FromRgb(0xF0, 0xED, 0xE6));
    private bool _darkCanvas;

    /// <summary>Koyu çizim zemini (ADR-0058): koyu arkaplan ve okunur açık mürekkep.</summary>
    public bool DarkCanvas
    {
        get => _darkCanvas;
        set
        {
            if (_darkCanvas != value)
            {
                _darkCanvas = value;
                InvalidateVisual();
            }
        }
    }
    private static readonly Color Accent = Color.FromRgb(0x1f, 0x6f, 0xd8);
    private static readonly IBrush HighlightFill = new SolidColorBrush(Accent, 0.12);

    private Scene? _scene;
    private ViewTransform _view = new(1, 0, 0);
    private bool _fitPending = true;
    private Point? _panFrom;
    private Point? _rightDown;
    private Point? _contentFrom;
    private IReadOnlyList<Box> _highlights = [];
    private IReadOnlyList<Box> _ghosts = [];
    private IReadOnlyList<IReadOnlyList<(double X, double Y)>> _wireHighlights = [];
    private Point? _bandFrom;
    private Point? _bandTo;
    private IReadOnlyList<(double X, double Y)>? _previewPath;
    private IReadOnlyList<(double X, double Y)> _previewMarkers = [];
    private IReadOnlyList<(double X, double Y)> _pinMarkers = [];
    private (double X, double Y)? _hoverPin;
    private Func<IDataTransfer, bool>? _acceptsDrop;
    private Box? _grid;
    private Box? _pendingFocus;
    private string? _imageBaseDirectory;
    private readonly Dictionary<string, (long Ticks, Bitmap? Image)> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Göreli logo/görsel yollarının çözümleneceği klasör (proje dosyasının klasörü).</summary>
    public string? ImageBaseDirectory
    {
        get => _imageBaseDirectory;
        set
        {
            if (!string.Equals(_imageBaseDirectory, value, StringComparison.Ordinal))
            {
                _imageBaseDirectory = value;
                InvalidateVisual();
            }
        }
    }

    public SceneView()
    {
        ClipToBounds = true;
        Focusable = true;
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = _scene is not null && _acceptsDrop?.Invoke(e.DataTransfer) == true
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (_scene is null || _acceptsDrop?.Invoke(e.DataTransfer) != true)
            {
                return;
            }

            var position = e.GetPosition(this);
            var (x, y) = _view.ToSheet(position.X, position.Y);
            Dropped?.Invoke(e.DataTransfer, x, y);
            e.Handled = true;
        });
    }

    /// <summary>Gösterilen sahne. Atanınca sayfa görünüme yeniden sığdırılır.</summary>
    public Scene? Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            _fitPending = true;
            InvalidateVisual();
        }
    }

    /// <summary>Seçili sembolün kutusu (sayfa koordinatları).</summary>
    public Box? Highlight
    {
        get => _highlights.Count > 0 ? _highlights[0] : null;
        set => Highlights = value is { } box ? [box] : [];
    }

    /// <summary>Seçili sembollerin kutuları (çoklu seçim).</summary>
    public IReadOnlyList<Box> Highlights
    {
        get => _highlights;
        set
        {
            _highlights = value;
            InvalidateVisual();
        }
    }

    /// <summary>Seçili tellerin yolları (sayfa mm); kalın vurgu çizgisiyle çizilir.</summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> WireHighlights
    {
        get => _wireHighlights;
        set
        {
            _wireHighlights = value;
            InvalidateVisual();
        }
    }

    /// <summary>Izgaranın çizileceği alan (sayfanın çizim alanı). Boşsa ızgara çizilmez.</summary>
    public Box? GridArea
    {
        get => _grid;
        set
        {
            _grid = value;
            InvalidateVisual();
        }
    }

    /// <summary>Izgara adımı (mm); sembollerin yapıştığı adımla aynıdır.</summary>
    public double GridStepMm { get; set; } = EditorSession.SnapMm;

    /// <summary>Sürükleme sırasında sembolün bırakılacağı yer.</summary>
    public Box? Ghost
    {
        get => _ghosts.Count > 0 ? _ghosts[0] : null;
        set => Ghosts = value is { } box ? [box] : [];
    }

    /// <summary>Birlikte sürüklenen sembollerin bırakılacağı yerler.</summary>
    public IReadOnlyList<Box> Ghosts
    {
        get => _ghosts;
        set
        {
            _ghosts = value;
            InvalidateVisual();
        }
    }

    /// <summary>Tel çekerken uçtan köşe noktalarından geçip imlece giden kesikli önizleme yolu (sayfa mm); boşsa çizilmez.</summary>
    public IReadOnlyList<(double X, double Y)>? PreviewPath
    {
        get => _previewPath;
        set
        {
            _previewPath = value;
            InvalidateVisual();
        }
    }

    /// <summary>Tel çekme modunda bağlanabilir uçlar (sayfa mm); halka olarak çizilir, ekranda sabit boyutludur.</summary>
    public IReadOnlyList<(double X, double Y)> PinMarkers
    {
        get => _pinMarkers;
        set
        {
            _pinMarkers = value;
            InvalidateVisual();
        }
    }

    /// <summary>İmlecin altındaki uç; dolu ve büyük çizilir.</summary>
    public (double X, double Y)? HoverPin
    {
        get => _hoverPin;
        set
        {
            if (_hoverPin != value)
            {
                _hoverPin = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>Verilmiş köşe noktaları (önizleme yolunda küçük daire olarak işaretlenir).</summary>
    public IReadOnlyList<(double X, double Y)> PreviewMarkers
    {
        get => _previewMarkers;
        set
        {
            _previewMarkers = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Sürüklenen veri bu alana bırakılabilir mi. Verilmezse alan bırakmayı kabul etmez. Bırakılınca
    /// <see cref="Dropped"/> sayfa noktasıyla (mm) bildirilir.
    /// </summary>
    public Func<IDataTransfer, bool>? AcceptsDrop
    {
        get => _acceptsDrop;
        set
        {
            _acceptsDrop = value;
            DragDrop.SetAllowDrop(this, value is not null);
        }
    }

    /// <summary>Kabul edilen veri sayfada bir noktaya bırakıldı (mm).</summary>
    public event Action<IDataTransfer, double, double>? Dropped;

    /// <summary>Son basışta basılı olan değiştirici tuşlar (Ctrl ile seçime ekleme).</summary>
    public KeyModifiers PressModifiers { get; private set; }

    /// <summary>
    /// Boş alanda sol tuşla çizilen seçim dikdörtgeni bırakıldı (sayfa mm). Abone yoksa boş alanda sol
    /// tuş da görünümü kaydırır.
    /// </summary>
    public event Action<Box>? RegionSelected;

    /// <summary>
    /// Sayfada bir noktaya basıldı (mm). Doğru dönerse sürükleme içeriğe aittir (sembol taşıma);
    /// yanlış dönerse görünüm kaydırılır.
    /// </summary>
    public Func<double, double, bool>? ContentPressed { get; set; }

    /// <summary>
    /// Sağ tuş sürüklenmeden bırakıldı (sayfa mm); bağlam menüsü içindir. Sağ tuşla sürükleme görünümü kaydırır ve
    /// menü açmaz.
    /// </summary>
    public event Action<double, double>? ContentContextRequested;

    /// <summary>Bağlam menüsünü verilen sayfa noktası için ister (görüntü aracı ve testler; fare sağ tuşuyla aynı yol).</summary>
    public void RequestContextMenu(double x, double y) => ContentContextRequested?.Invoke(x, y);

    /// <summary>Sayfada bir noktaya çift tıklandı (mm).</summary>
    public event Action<double, double>? ContentDoubleClicked;

    /// <summary>İçerik sürükleniyor: basılan noktadan itibaren mesafe (mm).</summary>
    public event Action<double, double>? ContentDragged;

    /// <summary>İçerik bırakıldı: basılan noktadan itibaren toplam mesafe (mm).</summary>
    public event Action<double, double>? ContentReleased;

    /// <summary>İmlecin sayfa üzerindeki konumu (mm) değiştiğinde bildirilir.</summary>
    public event Action<double, double>? SheetPointerMoved;

    /// <summary>Ekran pikselinin sayfa mm'si cinsinden karşılığı; tıklama toleransını zumdan bağımsız kılar.</summary>
    public double MmPerPixel => 1 / _view.Scale;

    /// <summary>Ölçeği değiştirmeden verilen kutuyu görünümün ortasına getirir.</summary>
    public void CenterOn(Box box)
    {
        if (_fitPending && _scene is not null && Bounds.Width > 0)
        {
            _view = ViewTransform.Fit(_scene.WidthMm, _scene.HeightMm, Bounds.Width, Bounds.Height);
            _fitPending = false;
        }

        _view = _view with
        {
            OffsetX = (Bounds.Width / 2) - ((box.X + (box.Width / 2)) * _view.Scale),
            OffsetY = (Bounds.Height / 2) - ((box.Y + (box.Height / 2)) * _view.Scale),
        };
        InvalidateVisual();
    }

    /// <summary>Verilen bölgeyi görünüme sığdırıp ortalar (antet önizlemesi gibi).</summary>
    public void FocusBox(Box area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _pendingFocus = area;
            _fitPending = false;
            InvalidateVisual();
            return;
        }

        ApplyFocus(area);
        InvalidateVisual();
    }

    private void ApplyFocus(Box area)
    {
        const double pad = 6.0;
        var sx = Bounds.Width / (area.Width + (2 * pad));
        var sy = Bounds.Height / (area.Height + (2 * pad));
        var scale = Math.Max(0.2, Math.Min(sx, sy));
        _view = new ViewTransform(
            scale,
            (Bounds.Width / 2) - ((area.X + (area.Width / 2)) * scale),
            (Bounds.Height / 2) - ((area.Y + (area.Height / 2)) * scale));
        _pendingFocus = null;
        _fitPending = false;
    }

    /// <summary>Sayfayı yeniden görünüme sığdırır.</summary>
    public void FitToView()
    {
        _pendingFocus = null;
        _fitPending = true;
        InvalidateVisual();
    }

    /// <summary>Sahneyi, yakınlaştırmayı ve kaydırmayı bozmadan değiştirir (düzenleme sonrası).</summary>
    public void UpdateScene(Scene? scene)
    {
        _scene = scene;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        // Arka plan her zaman çizilir; böylece boş alanda da fare olayları alınır.
        context.FillRectangle(_darkCanvas ? DarkBackdrop : Backdrop, new Rect(Bounds.Size));
        if (_scene is null)
        {
            return;
        }

        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            if (_pendingFocus is { } focus)
            {
                ApplyFocus(focus);
            }
            else if (_fitPending)
            {
                _view = ViewTransform.Fit(_scene.WidthMm, _scene.HeightMm, Bounds.Width, Bounds.Height);
                _fitPending = false;
            }
        }

        var matrix = Matrix.CreateScale(_view.Scale, _view.Scale) * Matrix.CreateTranslation(_view.OffsetX, _view.OffsetY);
        using (context.PushTransform(matrix))
        {
            context.FillRectangle(_darkCanvas ? DarkPaper : Brushes.White, new Rect(0, 0, _scene.WidthMm, _scene.HeightMm));
            DrawGrid(context);
            foreach (var item in _scene.Items)
            {
                Draw(context, item);
            }

            // Çerçeve kalınlığı ekranda sabit kalsın diye ölçeğe bölünür.
            var outline = new Pen(new SolidColorBrush(Accent), 1.5 / _view.Scale);
            foreach (var selected in _highlights)
            {
                context.DrawRectangle(HighlightFill, outline, Padded(selected));
            }

            var wirePen = new Pen(new SolidColorBrush(Accent), 3.0 / _view.Scale, lineCap: PenLineCap.Round);
            foreach (var path in _wireHighlights)
            {
                for (var i = 1; i < path.Count; i++)
                {
                    context.DrawLine(wirePen, new Point(path[i - 1].X, path[i - 1].Y), new Point(path[i].X, path[i].Y));
                }
            }

            var ring = new Pen(new SolidColorBrush(Accent), 1.5 / _view.Scale);
            foreach (var pin in _pinMarkers)
            {
                context.DrawEllipse(null, ring, new Point(pin.X, pin.Y), 4.5 / _view.Scale, 4.5 / _view.Scale);
            }

            if (_hoverPin is { } hover)
            {
                context.DrawEllipse(new SolidColorBrush(Accent), null, new Point(hover.X, hover.Y), 6.5 / _view.Scale, 6.5 / _view.Scale);
            }

            var dashed = new Pen(new SolidColorBrush(Accent), 1.5 / _view.Scale, DashStyle.Dash);
            foreach (var ghost in _ghosts)
            {
                context.DrawRectangle(null, dashed, Padded(ghost));
            }

            if (_previewPath is { Count: > 0 } preview)
            {
                for (var i = 1; i < preview.Count; i++)
                {
                    context.DrawLine(dashed, new Point(preview[i - 1].X, preview[i - 1].Y), new Point(preview[i].X, preview[i].Y));
                }

                context.DrawEllipse(new SolidColorBrush(Accent), null, new Point(preview[0].X, preview[0].Y), 0.8, 0.8);
            }

            foreach (var marker in _previewMarkers)
            {
                context.DrawEllipse(new SolidColorBrush(Accent), null, new Point(marker.X, marker.Y), 1.0, 1.0);
            }

            if (Band() is { } band)
            {
                context.DrawRectangle(HighlightFill, dashed, new Rect(band.X, band.Y, band.Width, band.Height));
            }
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _fitPending = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var position = e.GetPosition(this);
        _view = _view.ZoomAt(position.X, position.Y, e.Delta.Y > 0 ? 1.2 : 1 / 1.2);
        _fitPending = false;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var position = e.GetPosition(this);
        var (x, y) = _view.ToSheet(position.X, position.Y);
        if (e.ClickCount == 2 && _scene is not null)
        {
            ContentDoubleClicked?.Invoke(x, y);
            e.Handled = true;
            return;
        }

        // Sol tuş içeriğe (sembol) ya da seçim dikdörtgenine; orta ve sağ tuş görünümü kaydırır.
        PressModifiers = e.KeyModifiers;
        _rightDown = e.GetCurrentPoint(this).Properties.IsRightButtonPressed ? position : null;
        var left = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (left && _scene is not null && ContentPressed?.Invoke(x, y) == true)
        {
            _contentFrom = position;
        }
        else if (left && _scene is not null && RegionSelected is not null)
        {
            _bandFrom = _bandTo = position;
        }
        else
        {
            _panFrom = position;
        }

        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_contentFrom is { } start)
        {
            ContentDragged?.Invoke((position.X - start.X) / _view.Scale, (position.Y - start.Y) / _view.Scale);
        }
        else if (_bandFrom is not null)
        {
            _bandTo = position;
            InvalidateVisual();
        }
        else if (_panFrom is { } from)
        {
            _view = _view.Pan(position.X - from.X, position.Y - from.Y);
            _panFrom = position;
            InvalidateVisual();
        }

        var (x, y) = _view.ToSheet(position.X, position.Y);
        SheetPointerMoved?.Invoke(x, y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_rightDown is { } rightDown && e.InitialPressMouseButton == MouseButton.Right)
        {
            var at = e.GetPosition(this);
            _rightDown = null;
            if (Math.Abs(at.X - rightDown.X) < 4 && Math.Abs(at.Y - rightDown.Y) < 4 && _scene is not null)
            {
                var (cx, cy) = _view.ToSheet(at.X, at.Y);
                ContentContextRequested?.Invoke(cx, cy);
            }
        }

        if (_contentFrom is { } start)
        {
            var position = e.GetPosition(this);
            _contentFrom = null;
            ContentReleased?.Invoke((position.X - start.X) / _view.Scale, (position.Y - start.Y) / _view.Scale);
        }

        if (_bandFrom is not null)
        {
            var band = Band();
            _bandFrom = _bandTo = null;
            InvalidateVisual();
            if (band is { } region)
            {
                RegionSelected?.Invoke(region);
            }
        }

        _panFrom = null;
        e.Pointer.Capture(null);
    }

    // Seçim dikdörtgeni sayfa mm'sinde; 4 pikselden kısa sürükleme tıklama sayılır.
    private Box? Band()
    {
        if (_bandFrom is not { } a || _bandTo is not { } b || (Math.Abs(a.X - b.X) < 4 && Math.Abs(a.Y - b.Y) < 4))
        {
            return null;
        }

        var (x1, y1) = _view.ToSheet(a.X, a.Y);
        var (x2, y2) = _view.ToSheet(b.X, b.Y);
        return new Box(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    // Izgara noktaları yalnızca ekranda; çıktıya girmez. Noktalar birbirine 6 pikselden yakın düşerse adım
    // ikiye katlanır, böylece uzaklaştırınca alan gri bir lekeye dönmez.
    private void DrawGrid(DrawingContext context)
    {
        if (_grid is not { } area || GridStepMm <= 0)
        {
            return;
        }

        var step = GridStepMm;
        while (step * _view.Scale < 6)
        {
            step *= 2;
        }

        var size = 1.2 / _view.Scale;
        var fine = _darkCanvas ? DarkFineGrid : new SolidColorBrush(Color.FromRgb(0xc9, 0xc0, 0xa8));
        var coarse = _darkCanvas ? DarkCoarseGrid : new SolidColorBrush(Color.FromRgb(0x8f, 0x86, 0x70));
        for (var x = area.X; x <= area.Right + 0.001; x += step)
        {
            var majorX = Math.Abs(Math.IEEERemainder(x - area.X, step * 2)) < 0.001;
            for (var y = area.Y; y <= area.Bottom + 0.001; y += step)
            {
                var major = majorX && Math.Abs(Math.IEEERemainder(y - area.Y, step * 2)) < 0.001;
                var dot = major ? size * 1.4 : size;
                context.FillRectangle(major ? coarse : fine, new Rect(x - (dot / 2), y - (dot / 2), dot, dot));
            }
        }
    }

    private static Rect Padded(Box box) => new(box.X - 1.5, box.Y - 1.5, box.Width + 3, box.Height + 3);

    private void Draw(DrawingContext context, SceneItem item)
    {
        var ink = _darkCanvas ? DarkInk : Brushes.Black;
        switch (item)
        {
            case LineItem line:
                context.DrawLine(Pen(line.StrokeMm, line.Color, ink, line.Style), new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                break;

            case RectItem rect:
                context.DrawRectangle(null, Pen(rect.StrokeMm, null, ink), new Rect(rect.X, rect.Y, rect.Width, rect.Height));
                break;

            case CircleItem circle:
                context.DrawEllipse(
                    circle.Filled ? ink : null,
                    Pen(circle.StrokeMm, null, ink),
                    new Point(circle.X, circle.Y),
                    circle.Radius,
                    circle.Radius);
                break;

            case TextItem text when text.Text.Length > 0:
                DrawText(context, text, ink);
                break;

            case ImageItem image:
                DrawImage(context, image);
                break;
        }
    }

    private void DrawImage(DrawingContext context, ImageItem item)
    {
        if (ResolveBitmap(item.Path) is not { } bitmap || bitmap.Size.Width <= 0 || bitmap.Size.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(item.Width / bitmap.Size.Width, item.Height / bitmap.Size.Height);
        var width = bitmap.Size.Width * scale;
        var height = bitmap.Size.Height * scale;
        var x = item.X + ((item.Width - width) / 2);
        var y = item.Y + ((item.Height - height) / 2);
        context.DrawImage(bitmap, new Rect(x, y, width, height));
    }

    private const long MaxImageBytes = 20L * 1024 * 1024;
    private static readonly string[] AllowedImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif", ".ico"];

    private static bool IsUnsafeLocalImagePath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal)
        || (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsUnc);

    private Bitmap? ResolveBitmap(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || IsUnsafeLocalImagePath(rawPath))
        {
            return null;
        }

        string fullPath;
        try
        {
            var combined = Path.IsPathRooted(rawPath)
                ? rawPath
                : (_imageBaseDirectory is not null
                    ? Path.Combine(_imageBaseDirectory, rawPath)
                    : Path.Combine(AppContext.BaseDirectory, rawPath));
            if (IsUnsafeLocalImagePath(combined))
            {
                return null;
            }

            fullPath = Path.GetFullPath(combined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var ext = Path.GetExtension(fullPath);
        if (IsUnsafeLocalImagePath(fullPath)
            || !AllowedImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)
            || !File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length <= 0 || info.Length > MaxImageBytes)
            {
                return null;
            }

            var ticks = info.LastWriteTimeUtc.Ticks;
            if (_bitmaps.TryGetValue(fullPath, out var cached) && cached.Ticks == ticks)
            {
                return cached.Image;
            }

            cached.Image?.Dispose();
            var loaded = new Bitmap(fullPath);
            _bitmaps[fullPath] = (ticks, loaded);
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            return null;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var entry in _bitmaps.Values)
        {
            entry.Image?.Dispose();
        }

        _bitmaps.Clear();
    }

    private static void DrawText(DrawingContext context, TextItem text, IBrush ink)
    {
        var textBrush = text.Color is not null && Color.TryParse(text.Color, out var parsed)
            ? new SolidColorBrush(parsed)
            : ink;
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(text.FontFamily),
            text.SizeMm,
            textBrush);

        var x = text.Align switch
        {
            TextAlign.Center => text.X - (formatted.Width / 2),
            TextAlign.Right => text.X - formatted.Width,
            _ => text.X,
        };
        var origin = new Point(x, text.Y - formatted.Baseline);

        if (text.RotationDeg == 0)
        {
            context.DrawText(formatted, origin);
            return;
        }

        var rotation = Matrix.CreateTranslation(-text.X, -text.Y)
            * Matrix.CreateRotation(text.RotationDeg * Math.PI / 180)
            * Matrix.CreateTranslation(text.X, text.Y);
        using (context.PushTransform(rotation))
        {
            context.DrawText(formatted, origin);
        }
    }

    private static Pen Pen(
        double strokeMm,
        string? color,
        IBrush ink,
        Ake.Core.Model.LineStyle style = Ake.Core.Model.LineStyle.Solid)
    {
        var brush = color is not null && Color.TryParse(color, out var parsed) ? new SolidColorBrush(parsed) : ink;
        var s = Math.Max(0.15, strokeMm);
        IDashStyle? dash = style switch
        {
            Ake.Core.Model.LineStyle.Dashed => new DashStyle([3.0 / s, 1.5 / s], 0),
            Ake.Core.Model.LineStyle.DashDot => new DashStyle([4.0 / s, 1.0 / s, 0.8 / s, 1.0 / s], 0),
            Ake.Core.Model.LineStyle.Dotted => new DashStyle([0.6 / s, 1.0 / s], 0),
            _ => null,
        };
        return new Pen(brush, strokeMm, dash);
    }
}
