using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Library;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Editor;

/// <summary>Yeni proje ve dosya durumu, oturum tarafı (Faz 6.2, madde 10).</summary>
public sealed partial class EditorSession
{
    /// <summary>Proje henüz bir dosyaya kaydedilmedi mi (yeni proje).</summary>
    public bool IsUntitled => Project is not null && ProjectPath is null;

    /// <summary>Boş bir proje açar: uygulamayla gelen A3 yatay şablon ve bir boş sayfa. Dosyası yoktur; ilk kaydetmede yol sorulur. Açık proje kapanır; kaydedilmemiş değişiklikleri sormak pencerenin işidir.</summary>
    public Result NewProject(string name = "Yeni proje") => NewProjectCore(name, null, withCover: false, templateId: null);

    /// <summary>Açık projeyi kapatır ve proje önbelleklerini temizler; kütüphane ve ayarlar açık kalır.</summary>
    public void CloseProject()
    {
        Project = null;
        ProjectPath = null;
        Erc = null;
        _stack = null;
        _savedAuditCount = 0;
        _pdfBookmarkHints.Clear();
        _wireCache.Clear();
        _pinCache.Clear();
        _locateIndex = null;
        _clipboard = null;
        _version++;
    }

    private Result NewProjectCore(string name, IReadOnlyDictionary<string, string>? properties, bool withCover, SheetTemplateId? templateId)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(new Error("editor.ad_bos", "Projenin adı boş olamaz."));
        }

        // Şablon: hazır şablonlardan seçilen (yoksa varsayılan A3 yatay).
        var template = templateId is { } chosen && StandardSheetTemplates.Find(chosen) is { } standard
            ? Result.Success(standard)
            : DefaultTemplate();
        if (template.IsFailure)
        {
            return Result.Failure(template.Error!);
        }

        var created = Project.Create(
            ProjectId.New(),
            name.Trim(),
            pages: withCover
                // Proje oluşturma penceresinden gelen proje kapaklı başlar: önce kapak (proje bilgileri sağ tıkla girilir), sonra boş şema sayfası.
                ? [new Page(PageId.New(), "1", "Kapak", templateId: template.Value.Id, kind: PageKind.Cover), new Page(PageId.New(), "2", "Sayfa", templateId: template.Value.Id)]
                : [new Page(PageId.New(), "1", "Sayfa 1", templateId: template.Value.Id)],
            sheetTemplates: [template.Value],
            properties: properties);
        if (created.IsFailure)
        {
            return Result.Failure(created.Error!);
        }

        Project = created.Value;
        ProjectPath = null;
        _pdfBookmarkHints.Clear();
        Erc = ErcEngine.Default.Run(created.Value);
        _stack = new CommandStack(created.Value);
        _savedAuditCount = 0;
        _version++;
        return Result.Success();
    }

    /// <summary>
    /// Okunmuş bir PDF çizimini açık projenin sonuna sayfa sayfa ekler (serbest grafik, ADR-0028); tek adımda geri alınır.
    /// Çerçevesi atılmış içerik, projenin şablonunun çizim alanına hizalanır. Eklenen sayfa sayısı döner.
    /// </summary>
    public Result<int> ImportPdf(PdfDrawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        if (Project is null)
        {
            return Result.Failure<int>(new Error("editor.proje_yok", "Proje açık değil."));
        }

        var template = Project.SheetTemplates.FirstOrDefault();
        (double, double)? origin = template is null ? null : (SheetGeometry.For(template).DrawingArea.X, SheetGeometry.For(template).DrawingArea.Y);
        var beforeCount = Project.Pages.Count;
        var plan = PdfImport.Plan(Project, drawing, origin);
        if (plan.IsFailure)
        {
            return Result.Failure<int>(plan.Error!);
        }

        var executed = Execute(plan.Value);
        if (executed.IsFailure)
        {
            return Result.Failure<int>(executed.Error!);
        }

        for (var i = 0; i < drawing.Pages.Count && beforeCount + i < Project.Pages.Count; i++)
        {
            if (drawing.Pages[i].BookmarkComponents is { Count: > 0 } hints)
            {
                _pdfBookmarkHints[Project.Pages[beforeCount + i].Id] = hints;
            }
        }

        return Result.Success(drawing.Pages.Count);
    }

    /// <summary>
    /// DXF/DWG veya .dxf.zip çizim dosyasını (ya da klasörünü) her çizim bir sayfa olacak biçimde
    /// düzenlenebilir serbest grafik (<see cref="PageGraphic"/>) olarak projeye alır (ADR-0057).
    /// </summary>
    public Result<int> ImportDxf(string path, bool keepFrame = false)
    {
        var read = DxfPageImport.Read(path, keepFrame);
        return read.IsFailure ? Result.Failure<int>(read.Error!) : ImportDxf(read.Value);
    }

    /// <summary>Daha önce okunmuş <see cref="DxfPageDrawing"/> çizimini yeni sayfalar olarak ekler (ADR-0057).</summary>
    public Result<int> ImportDxf(DxfPageDrawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        if (Project is null)
        {
            return Result.Failure<int>(NoProject().Error!);
        }

        var template = Project.SheetTemplates.FirstOrDefault();
        (double X, double Y, double Width, double Height)? area = null;
        if (template is not null)
        {
            var da = SheetGeometry.For(template).DrawingArea;
            area = (da.X, da.Y, da.Width, da.Height);
        }
        var plan = DxfPageImport.Plan(Project, drawing, area, template?.Id);
        if (plan.IsFailure)
        {
            return Result.Failure<int>(plan.Error!);
        }

        var executed = Execute(plan.Value);
        return executed.IsFailure ? Result.Failure<int>(executed.Error!) : Result.Success(drawing.Pages.Count);
    }

    /// <summary>
    /// SEE Electrical dışa aktarım listesini (.xlsx / .csv) cihaz, I/O ve klemens komutları olarak projeye alır (ADR-0057).
    /// </summary>
    public Result<SeeListImportSummary> ImportSeeList(string path)
    {
        if (Project is null)
        {
            return Result.Failure<SeeListImportSummary>(NoProject().Error!);
        }

        var planned = SeeListImport.FromFile(Project, path);
        if (planned.IsFailure)
        {
            return Result.Failure<SeeListImportSummary>(planned.Error!);
        }

        var executed = Execute(planned.Value.Transaction);
        return executed.IsFailure
            ? Result.Failure<SeeListImportSummary>(executed.Error!)
            : Result.Success(planned.Value.Summary);
    }

    /// <summary>
    /// Dışarıdan hazırlanmış bir komutu (ya da <see cref="Transaction"/>'ı) editörün kendi işlemleri gibi çalıştırır: geçmişe
    /// kayıt düşer, geri alınır, ERC yenilenir. Betikle kurma ve toplu içe aktarma içindir (ADR-0031).
    /// </summary>
    public Result Run(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Project is null ? NoProject() : Execute(command);
    }

    /// <summary>Uygulamayla gelen varsayılan sayfa şablonu (A3 yatay, 10 kolon).</summary>
    internal static Result<SheetTemplate> DefaultTemplate() => Result.Success(StandardSheetTemplates.A3Landscape);
}
