using Ake.Core;
using Ake.Core.Model;
using Ake.Library;
using Ake.Render;

namespace Ake.Editor;

/// <summary>
/// PDF / serbest grafik sayfalarından cihaz, tel ucu ve bağlantı tanıma; modele dönüştürme ve
/// kütüphaneye aktarma, oturum tarafı.
/// </summary>
public sealed partial class EditorSession
{
    private readonly Dictionary<PageId, IReadOnlyList<PdfBookmarkComponent>> _pdfBookmarkHints = [];

    /// <summary>Verilen sayfada en az bir serbest grafik öğesi var mı.</summary>
    public bool PageHasGraphics(int pageIndex)
    {
        var page = PageAt(pageIndex);
        return page.IsSuccess && Project!.Graphics.Any(g => g.PageId == page.Value.Id);
    }

    /// <summary>Projedeki grafik içeren sayfa indeksleri.</summary>
    public IReadOnlyList<int> GraphicPageIndices()
    {
        if (Project is null)
        {
            return [];
        }

        var pagesWithGraphics = Project.Graphics.Select(g => g.PageId).ToHashSet();
        var indices = new List<int>();
        for (var i = 0; i < Project.Pages.Count; i++)
        {
            if (Project.Pages[i].Kind == PageKind.Schematic && pagesWithGraphics.Contains(Project.Pages[i].Id))
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    /// <summary>Tek bir sayfadaki serbest grafiklerden cihazları, tel uçlarını ve bağlantıları tespit eder.</summary>
    public Result<PageRecognitionResult> RecognizePage(int pageIndex)
    {
        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return Result.Failure<PageRecognitionResult>(page.Error!);
        }

        var hints = _pdfBookmarkHints.GetValueOrDefault(page.Value.Id);
        return Result.Success(GraphicRecognition.RecognizePage(Project!, page.Value, hints));
    }

    /// <summary>Projedeki grafik içeren tüm şema sayfalarında cihazları, tel uçlarını ve bağlantıları tespit eder.</summary>
    public Result<IReadOnlyList<PageRecognitionResult>> RecognizeAllGraphicPages()
    {
        if (Project is null)
        {
            return Result.Failure<IReadOnlyList<PageRecognitionResult>>(NoProject().Error!);
        }

        return Result.Success(GraphicRecognition.RecognizeProject(Project, _pdfBookmarkHints));
    }

    /// <summary>
    /// Onaylanan cihaz, tel ucu ve bağlantıları geri alınabilir tek işlemde elektriksel modele dönüştürür.
    /// </summary>
    public Result<RecognitionConversionSummary> ConvertRecognized(
        IReadOnlyList<RecognizedDevice> devices,
        IReadOnlyList<RecognizedWire> wires,
        bool removeConsumedGraphics = true)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(wires);

        if (Project is null)
        {
            return Result.Failure<RecognitionConversionSummary>(NoProject().Error!);
        }

        var planned = GraphicRecognition.PlanConversion(
            Project,
            devices,
            wires,
            page =>
            {
                var template = Project.TemplateFor(page);
                if (template is null)
                {
                    return (0.0, 0.0);
                }

                var area = SheetGeometry.For(template).DrawingArea;
                return (area.X, area.Y);
            },
            removeConsumedGraphics);
        if (planned.IsFailure)
        {
            return Result.Failure<RecognitionConversionSummary>(planned.Error!);
        }

        var executed = Execute(planned.Value.Transaction);
        return executed.IsFailure
            ? Result.Failure<RecognitionConversionSummary>(executed.Error!)
            : Result.Success(planned.Value.Summary);
    }

    /// <summary>Seçilen cihazların sembol ve ürün kayıtlarını açık kütüphaneye aktarır.</summary>
    public Result<RecognitionLibrarySummary> ExportRecognizedToLibrary(
        IReadOnlyList<RecognizedDevice> devices,
        string? source = null)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (_library is null)
        {
            return Result.Failure<RecognitionLibrarySummary>(
                new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."));
        }

        var originSource = string.IsNullOrWhiteSpace(source)
            ? (ProjectPath is { Length: > 0 } p ? $"Proje: {Path.GetFileName(p)}" : "PDF / grafikten aktarıldı")
            : source.Trim();
        return GraphicRecognition.ExportToLibrary(
            _library, devices, SymbolPalette.UserCategory, originSource, "Kullanıcı çizimi");
    }
}
