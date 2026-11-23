using Ake.Core;
using Ake.Render;
using Ake.Reports;

namespace Ake.Editor;

/// <summary>Proje PDF, DXF/DWG, mühendislik raporları (XLSX/CSV) ve baskıya hazır etiket PDF çıktısı (ADR-0056).</summary>
public sealed partial class EditorSession
{
    /// <summary>Projenin bütün sayfalarını çok sayfalı ve yer imli PDF dosyasına yazar.</summary>
    public Result ExportPdf(string path, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Project is null)
        {
            return NoProject();
        }

        var scenes = new List<Scene>(Project.Pages.Count);
        for (var i = 0; i < Project.Pages.Count; i++)
        {
            var scene = SceneFor(i, today);
            if (scene.IsFailure)
            {
                return Result.Failure(scene.Error!);
            }

            scenes.Add(scene.Value);
        }

        var baseDirectory = ProjectPath is null ? null : Path.GetDirectoryName(ProjectPath);
        return PdfExporter.Save(scenes, path, baseDirectory, Project.Name);
    }

    /// <summary>Tek bir sayfayı DXF ya da DWG olarak kaydeder.</summary>
    public Result ExportCadPage(int pageIndex, string path, CadFormat format, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Project is null)
        {
            return NoProject();
        }

        var scene = SceneFor(pageIndex, today);
        return scene.IsFailure
            ? Result.Failure(scene.Error!)
            : CadExporter.Save(scene.Value, path, format);
    }

    /// <summary>Projedeki bütün sayfaları seçilen klasöre DXF veya DWG dosyaları olarak yazar; yazılan sayfa sayısını döner.</summary>
    public Result<int> ExportAllCadPages(string directory, CadFormat format, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (Project is null)
        {
            return Result.Failure<int>(NoProject().Error!);
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<int>(new Error("render.dosya_yazilamadi", $"'{directory}' oluşturulamadı: {ex.Message}"));
        }

        var ext = format == CadFormat.Dwg ? "dwg" : "dxf";
        for (var i = 0; i < Project.Pages.Count; i++)
        {
            var page = Project.Pages[i];
            var safeNumber = string.Concat(page.Number.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var filePath = Path.Combine(directory, $"sayfa-{safeNumber}.{ext}");
            var saved = ExportCadPage(i, filePath, format, today);
            if (saved.IsFailure)
            {
                return Result.Failure<int>(saved.Error!);
            }
        }

        return Result.Success(Project.Pages.Count);
    }

    /// <summary>Tüm mühendislik raporlarını (ve PLC çapraz referansını) tek Excel (.xlsx) dosyasına yazar.</summary>
    public Result ExportReportsXlsx(string path, IReadOnlyList<string>? reportNames = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Project is null)
        {
            return NoProject();
        }

        var names = reportNames is { Count: > 0 } ? reportNames : [.. ReportCatalog.Names, .. ReportCatalog.SpecialNames];
        var tables = new List<ReportTable>(names.Count);
        foreach (var name in names)
        {
            var built = ReportCatalog.Build(name, Project);
            if (built.IsFailure)
            {
                return Result.Failure(built.Error!);
            }

            tables.Add(built.Value);
        }

        return XlsxReport.Save(tables, path);
    }

    /// <summary>Seçili raporu UTF-8 BOM'lu CSV dosyasına yazar.</summary>
    public Result ExportReportCsv(string reportName, string path, char separator = ',')
    {
        ArgumentNullException.ThrowIfNull(reportName);
        ArgumentNullException.ThrowIfNull(path);
        if (Project is null)
        {
            return NoProject();
        }

        var built = ReportCatalog.Build(reportName, Project);
        return built.IsFailure ? Result.Failure(built.Error!) : CsvReport.Save(built.Value, path, separator);
    }

    /// <summary>Kablo ve tel etiketlerini kesim kılavuz çizgili, baskıya hazır A4 PDF olarak yazar (Faz 6c-3).</summary>
    public Result ExportLabelsPdf(string path, int columns = 3, int rowsPerPage = 10)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Project is null ? NoProject() : LabelPdfExporter.Save(Project, path, columns, rowsPerPage);
    }
}
