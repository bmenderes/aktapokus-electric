using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Editor;

/// <summary>
/// Editörün açık projesi, değişiklik geçmişi ve ondan türetilen görünümler. Arayüz kütüphanesine
/// bağlı değildir; pencere yalnızca bunu gösterir ve çağırır, böylece davranış pencere açmadan test
/// edilir. Her değişiklik bir Command'dır (ADR-0001).
/// </summary>
public sealed partial class EditorSession(string actor = "editor")
{
    /// <summary>Semboller bu adıma yapışarak taşınır (mm).</summary>
    public const double SnapMm = 5;

    private const double HitToleranceMm = 1;

    private CommandStack? _stack;
    private int _savedAuditCount;

    // Her başarılı değişiklikte artar; sayfa başına hesaplanan tel yolları buna göre tazelenir.
    private int _version;

    public Project? Project { get; private set; }

    public string? ProjectPath { get; private set; }

    public ErcReport? Erc { get; private set; }

    public bool CanUndo => _stack?.CanUndo ?? false;

    public bool CanRedo => _stack?.CanRedo ?? false;

    /// <summary>Son kaydetmeden beri değişiklik (geri alma dahil) yapıldı mı.</summary>
    public bool IsDirty => _stack is not null && _stack.Audit.Count != _savedAuditCount;

    /// <summary>Sayfa listesinde gösterilen satırlar: numara ve başlık.</summary>
    public IReadOnlyList<string> PageLabels =>
        Project is null ? [] : [.. Project.Pages.Select(page => $"{page.Number}  {page.Title}")];

    /// <summary>Durum satırı: tek satır, proje özeti ve ERC sonucu.</summary>
    public string Status
    {
        get
        {
            if (Project is null || Erc is null)
            {
                return "Proje açık değil";
            }

            return $"{Project.Name}  ·  {Project.Pages.Count} sayfa  ·  {Project.Devices.Count(device => device.Kind != DeviceKind.Junction)} cihaz  ·  "
                + $"{Erc.Count(Severity.Error)} hata, {Erc.Count(Severity.Warning)} uyarı"
                + (IsDirty ? "  ·  kaydedilmedi" : string.Empty);
        }
    }

    /// <summary>Projeyi açar. Başarısız olursa önceki proje açık kalır.</summary>
    public Result Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var cleaned = path.Trim().Trim('"');
        if (cleaned.Length == 0)
        {
            return Result.Failure(new Error("editor.yol_bos", "Proje dosyasının yolu boş."));
        }

        var loaded = ProjectFile.Load(cleaned);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Error!);
        }

        Project = loaded.Value;
        ProjectPath = Path.GetFullPath(cleaned);
        _pdfBookmarkHints.Clear();
        Erc = ErcEngine.Default.Run(loaded.Value);
        _stack = new CommandStack(loaded.Value);
        _savedAuditCount = 0;
        _version++;
        return Result.Success();
    }

    public Result<Scene> SceneFor(int pageIndex, DateOnly today)
    {
        var page = PageAt(pageIndex);
        return page.IsFailure
            ? Result.Failure<Scene>(page.Error!)
            : SheetRenderer.Render(Project!, page.Value, today);
    }

    /// <summary>Sayfanın çizim alanı (ızgara buraya çizilir); sayfa ya da şablon yoksa <c>null</c>.</summary>
    public Box? DrawingAreaFor(int pageIndex)
    {
        var page = PageAt(pageIndex);
        return page.IsSuccess && Project!.TemplateFor(page.Value) is { } template
            ? SheetGeometry.For(template).DrawingArea
            : null;
    }

    /// <summary>Sayfa elle çizilen şema sayfası mı (nokta ızgarası ve şema ekleme komutları içindir).</summary>
    public bool IsSchematicPage(int pageIndex) =>
        PageAt(pageIndex) is { IsSuccess: true } page && page.Value.Kind == PageKind.Schematic;

    /// <summary>Sayfadaki sembol kutuları, sayfa koordinatlarında.</summary>
    public IReadOnlyList<PlacedBox> BoxesFor(int pageIndex)
    {
        var page = PageAt(pageIndex);
        return page.IsFailure ? [] : SheetRenderer.SymbolBoxes(Project!, page.Value);
    }

    /// <summary>Verilen sayfa noktasındaki sembol; üst üste gelenlerde en küçük kutu kazanır.</summary>
    public PlacedBox? HitTest(int pageIndex, double sheetX, double sheetY) =>
        BoxesFor(pageIndex)
            .Where(box => sheetX >= box.Bounds.X - HitToleranceMm
                && sheetX <= box.Bounds.Right + HitToleranceMm
                && sheetY >= box.Bounds.Y - HitToleranceMm
                && sheetY <= box.Bounds.Bottom + HitToleranceMm)
            .OrderBy(box => box.Bounds.Width * box.Bounds.Height)
            .FirstOrDefault();

    /// <summary>Sürükleme mesafesini yapışma adımına yuvarlar.</summary>
    public static (double X, double Y) Snap(double deltaX, double deltaY) =>
        (Math.Round(deltaX / SnapMm) * SnapMm, Math.Round(deltaY / SnapMm) * SnapMm);

    /// <summary>
    /// Sembolü, yapışma adımına yuvarlanmış mesafe kadar taşır. Yuvarlanan mesafe sıfırsa hiçbir şey
    /// yapmaz ve geçmişe kayıt düşmez.
    /// </summary>
    public Result Move(PlacementId placementId, double deltaX, double deltaY)
    {
        var placement = Find(placementId);
        if (placement.IsFailure)
        {
            return Result.Failure(placement.Error!);
        }

        var (dx, dy) = Snap(deltaX, deltaY);
        return dx == 0 && dy == 0
            ? Result.Success()
            : Execute(new MovePlacement(
                placementId, placement.Value.X + dx, placement.Value.Y + dy, placement.Value.RotationDeg));
    }

    /// <summary>Sembolü ilk ucu çevresinde saat yönünde 90 derece döndürür.</summary>
    public Result Rotate(PlacementId placementId)
    {
        var placement = Find(placementId);
        return placement.IsFailure
            ? Result.Failure(placement.Error!)
            : Execute(new MovePlacement(
                placementId, placement.Value.X, placement.Value.Y, (placement.Value.RotationDeg + 90) % 360));
    }

    /// <summary>Device tag'lerini projenin tag şemasına göre yeniden numaralar; tek adımda geri alınır.</summary>
    public Result RenumberTags() => Project is null ? NoProject() : ExecuteIfAny(Renumbering.Tags(Project));

    /// <summary>Tel numaralarını potansiyel başına yeniden verir; tek adımda geri alınır.</summary>
    public Result RenumberWires() =>
        Project is null ? NoProject() : ExecuteIfAny(Renumbering.Wires(Project, Project.WireNumbering));

    /// <summary>Yalnızca numarası olmayan telleri numaralar, mevcut numaralara dokunmaz ve kaldığı yerden sürer (ADR-0041).</summary>
    public Result NumberNewWires() =>
        Project is null ? NoProject() : ExecuteIfAny(Renumbering.NewWires(Project));

    /// <summary>Tel numaralandırma ayarlarını (kod, başlangıç, adım, basamak, şema) değiştirir; mevcut numaralar değişmez.</summary>
    public Result SetWireNumbering(WireNumberSettings settings) =>
        Project is null ? NoProject() : Execute(new SetWireNumbering(settings));

    /// <summary>Tag şemasını değiştirir ve tag'leri yeni şemaya göre numaralar; tek adımda geri alınır.</summary>
    public Result ChangeTagScheme(TagScheme scheme) =>
        Project is null ? NoProject() : ExecuteIfAny(Renumbering.ChangeTagScheme(Project, scheme));

    public Result Undo() => _stack is null ? NoProject() : Refreshed(_stack.Undo());

    public Result Redo() => _stack is null ? NoProject() : Refreshed(_stack.Redo());

    /// <summary>Projeyi açıldığı dosyaya yazar ve o zamandan beri yapılanları audit log'a ekler.</summary>
    /// <remarks>Yeni projenin henüz dosyası yoktur; o zaman <c>editor.dosya_yok</c> döner ve Farklı kaydet gerekir.</remarks>
    public Result Save() =>
        Project is null ? NoProject()
        : ProjectPath is null ? Result.Failure(new Error("editor.dosya_yok", "Proje henüz kaydedilmedi; Farklı kaydet ile bir dosya seçin."))
        : SaveTo(ProjectPath);

    /// <summary>
    /// Projeyi yeni bir dosyaya yazar ve bundan sonra o dosyayla çalışır. Kaydedilmemiş değişikliklerin
    /// audit kaydı yeni dosyanın yanına yazılır.
    /// </summary>
    public Result SaveAs(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Project is null)
        {
            return NoProject();
        }

        var cleaned = path.Trim().Trim('"');
        return cleaned.Length == 0
            ? Result.Failure(new Error("editor.yol_bos", "Proje dosyasının yolu boş."))
            : SaveTo(Path.GetFullPath(cleaned));
    }

    private Result SaveTo(string path)
    {
        var saved = ProjectFile.Save(Project!, path);
        if (saved.IsFailure)
        {
            return saved;
        }

        ProjectPath = path;
        var pending = _stack!.Audit.Skip(_savedAuditCount).ToList();
        _savedAuditCount = _stack.Audit.Count;
        return pending.Count == 0 ? Result.Success() : AuditLog.Append(AuditLog.PathFor(path), pending);
    }

    private Result Execute(ICommand command) =>
        _stack is null ? NoProject() : Refreshed(_stack.Execute(command, ChangeSource.User, actor));

    // Değişikliği olmayan işlem geri alma listesine boş adım bırakmasın.
    private Result ExecuteIfAny(Transaction transaction) =>
        transaction.Commands.Count == 0 ? Result.Success() : Execute(transaction);

    private Result Refreshed(Result result)
    {
        if (result.IsSuccess && Project is not null)
        {
            _version++;
            Erc = ErcEngine.Default.Run(Project);
        }

        return result;
    }

    private Result<Placement> Find(PlacementId id)
    {
        if (Project is null)
        {
            return Result.Failure<Placement>(new Error("editor.proje_yok", "Proje açık değil."));
        }

        return Project.Placements.FirstOrDefault(placement => placement.Id == id) is { } placement
            ? Result.Success(placement)
            : Result.Failure<Placement>(new Error("placement.yok", $"Yerleşim modelde yok ({id})."));
    }

    private Result<Page> PageAt(int pageIndex)
    {
        if (Project is null)
        {
            return Result.Failure<Page>(new Error("editor.proje_yok", "Proje açık değil."));
        }

        return pageIndex < 0 || pageIndex >= Project.Pages.Count
            ? Result.Failure<Page>(new Error("editor.sayfa_yok", $"Sayfa yok: {pageIndex + 1}"))
            : Result.Success(Project.Pages[pageIndex]);
    }

    private static Result NoProject() => Result.Failure(new Error("editor.proje_yok", "Proje açık değil."));
}

/// <summary>
/// Sayfa koordinatını (mm) ekran koordinatına çeviren görünüm dönüşümü: ölçek ve kaydırma.
/// </summary>
public readonly record struct ViewTransform(double Scale, double OffsetX, double OffsetY)
{
    public const double MinScale = 0.2;
    public const double MaxScale = 60;

    /// <summary>Sayfayı görünüme, kenar payı bırakarak ve ortalayarak sığdırır.</summary>
    public static ViewTransform Fit(double sheetWidth, double sheetHeight, double viewWidth, double viewHeight, double margin = 8)
    {
        if (sheetWidth <= 0 || sheetHeight <= 0 || viewWidth <= 2 * margin || viewHeight <= 2 * margin)
        {
            return new ViewTransform(1, 0, 0);
        }

        var scale = Math.Min((viewWidth - (2 * margin)) / sheetWidth, (viewHeight - (2 * margin)) / sheetHeight);
        return new ViewTransform(
            scale, (viewWidth - (sheetWidth * scale)) / 2, (viewHeight - (sheetHeight * scale)) / 2);
    }

    /// <summary>İmlecin altındaki sayfa noktası yerinde kalacak şekilde yakınlaştırır.</summary>
    public ViewTransform ZoomAt(double screenX, double screenY, double factor)
    {
        var scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        var applied = scale / Scale;
        return new ViewTransform(
            scale, screenX - ((screenX - OffsetX) * applied), screenY - ((screenY - OffsetY) * applied));
    }

    public ViewTransform Pan(double deltaX, double deltaY) => this with { OffsetX = OffsetX + deltaX, OffsetY = OffsetY + deltaY };

    public (double X, double Y) ToSheet(double screenX, double screenY) =>
        ((screenX - OffsetX) / Scale, (screenY - OffsetY) / Scale);
}
