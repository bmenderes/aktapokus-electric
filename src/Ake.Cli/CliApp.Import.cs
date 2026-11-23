using System.CommandLine;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Library;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Cli;

/// <summary><c>ake import</c>: başka bir araçtan gelen çizimleri projeye serbest grafik olarak alır (ADR-0028).</summary>
internal static partial class CliApp
{
    private static Command ImportCommand(TextWriter output, TextWriter error)
    {
        var command = new Command("import", "Başka bir araçtan gelen çizim ve listeleri projeye alır");
        command.Subcommands.Add(ImportPdfCommand(output, error));
        command.Subcommands.Add(ImportDxfCommand(output, error));
        command.Subcommands.Add(ImportListCommand(output, error));
        command.Subcommands.Add(ImportRecognizeCommand(output, error));
        return command;
    }

    private static Command ImportDxfCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json); sayfalar sonuna eklenir" };
        var dxf = new Argument<string>("yol") { Description = "DXF/DWG/.dxf.zip dosyası veya çizimleri içeren klasör" };
        var keepFrame = new Option<bool>("--ramkayi-koru")
        {
            Description = "DXF/DWG'deki dış çerçeveyi koru (varsayılan: dış çerçeve çizgileri atılır)",
        };
        var dryRun = new Option<bool>("--dry-run") { Description = "Değişiklikleri göster, dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var command = new Command("dxf", "DXF/DWG çizimlerini düzenlenebilir serbest grafik olarak projeye sayfa sayfa ekler");
        command.Arguments.Add(project);
        command.Arguments.Add(dxf);
        command.Options.Add(keepFrame);
        command.Options.Add(dryRun);
        command.Options.Add(actor);
        command.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var drawing = DxfPageImport.Read(parse.GetValue(dxf)!, parse.GetValue(keepFrame));
            if (drawing.IsFailure)
            {
                return Fail(error, drawing.Error!);
            }

            var template = loaded.Value.SheetTemplates.FirstOrDefault();
            (double, double, double, double)? area = template is null ? null : (
                SheetGeometry.For(template).DrawingArea.X,
                SheetGeometry.For(template).DrawingArea.Y,
                SheetGeometry.For(template).DrawingArea.Width,
                SheetGeometry.For(template).DrawingArea.Height);
            var plan = DxfPageImport.Plan(loaded.Value, drawing.Value, area);
            if (plan.IsFailure)
            {
                return Fail(error, plan.Error!);
            }

            var shapes = drawing.Value.Pages.Sum(page => page.Shapes.Count);
            output.WriteLine($"{drawing.Value.Pages.Count} sayfa, {shapes} grafik.");
            return Commit(loaded.Value, projectPath, plan.Value, parse.GetValue(dryRun), parse.GetValue(actor), output, error);
        });
        return command;
    }

    private static Command ImportListCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var listPath = new Argument<string>("liste") { Description = "SEE Electrical dışa aktarım Excel (.xlsx) veya CSV dosyası" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Değişiklikleri göster, dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var command = new Command("list", "Excel/CSV cihaz, ada ve I/O listelerini projeye aktarır");
        command.Arguments.Add(project);
        command.Arguments.Add(listPath);
        command.Options.Add(dryRun);
        command.Options.Add(actor);
        command.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var parsed = SeeListImport.FromFile(loaded.Value, parse.GetValue(listPath)!);
            if (parsed.IsFailure)
            {
                return Fail(error, parsed.Error!);
            }

            var s = parsed.Value.Summary;
            output.WriteLine($"{s.DevicesAdded} yeni cihaz, {s.DevicesUpdated} güncellenen cihaz, {s.ModulesAdded} modül, {s.ChannelsAdded} kanal, {s.TerminalsAdded} klemens.");
            return Commit(loaded.Value, projectPath, parsed.Value.Transaction, parse.GetValue(dryRun), parse.GetValue(actor), output, error);
        });
        return command;
    }

    private static Command ImportPdfCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json); sayfalar sonuna eklenir" };
        var pdf = new Argument<string>("pdf") { Description = "Vektör PDF (başka bir E-CAD aracının çıktısı)" };
        var keepFrame = new Option<bool>("--ramkayi-koru")
        {
            Description = "PDF'teki çerçeve, kolon harfleri ve anteti de al (varsayılan: at, projenin şablonu çizilir)",
        };
        var createNew = new Option<bool>("--yeni") { Description = "Varsa üzerine değil, A3 yatay şablonlu yeni boş proje oluşturur" };
        var recognize = new Option<bool>("--tani") { Description = "Alınan grafiklerden cihazları, tel uçlarını ve bağlantıları tanıyıp modele çevirir" };
        var libraryPath = new Option<string?>("--kutuphane") { Description = "--tani ile bulunan sembol ve ürünleri bu kütüphaneye (.db) aktarır" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Değişiklikleri göster, dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var command = new Command("pdf", "PDF sayfalarını düzenlenebilir serbest grafik olarak projeye sayfa sayfa ekler");
        command.Arguments.Add(project);
        command.Arguments.Add(pdf);
        command.Options.Add(keepFrame);
        command.Options.Add(createNew);
        command.Options.Add(recognize);
        command.Options.Add(libraryPath);
        command.Options.Add(dryRun);
        command.Options.Add(actor);
        command.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var pdfPath = parse.GetValue(pdf)!;
            var loaded = parse.GetValue(createNew)
                ? Project.Create(ProjectId.New(), Path.GetFileNameWithoutExtension(pdfPath), sheetTemplates: [StandardSheetTemplates.A3Landscape])
                : ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var drawing = PdfImport.Read(pdfPath, parse.GetValue(keepFrame));
            if (drawing.IsFailure)
            {
                return Fail(error, drawing.Error!);
            }

            var template = loaded.Value.SheetTemplates.FirstOrDefault();
            (double, double)? origin = template is null ? null : (SheetGeometry.For(template).DrawingArea.X, SheetGeometry.For(template).DrawingArea.Y);
            var existingPageCount = loaded.Value.Pages.Count;
            var plan = PdfImport.Plan(loaded.Value, drawing.Value, origin);
            if (plan.IsFailure)
            {
                return Fail(error, plan.Error!);
            }

            var shapes = drawing.Value.Pages.Sum(page => page.Shapes.Count);
            output.WriteLine(
                $"{drawing.Value.Pages.Count} sayfa, {shapes} grafik; {drawing.Value.DroppedAsFrame} çerçeve öğesi atıldı, "
                + $"{drawing.Value.IgnoredImages} görsel alınmadı.");

            var isDryRun = parse.GetValue(dryRun);
            var who = parse.GetValue(actor) ?? Environment.UserName;
            if (!parse.GetValue(recognize))
            {
                return Commit(loaded.Value, projectPath, plan.Value, isDryRun, who, output, error);
            }

            var stack = new CommandStack(loaded.Value);
            var imported = stack.Execute(plan.Value, ChangeSource.Import, who);
            if (imported.IsFailure)
            {
                return Fail(error, imported.Error!);
            }

            var hints = new Dictionary<PageId, IReadOnlyList<PdfBookmarkComponent>>();
            for (var i = 0; i < drawing.Value.Pages.Count && existingPageCount + i < loaded.Value.Pages.Count; i++)
            {
                if (drawing.Value.Pages[i].BookmarkComponents is { Count: > 0 } comps)
                {
                    hints[loaded.Value.Pages[existingPageCount + i].Id] = comps;
                }
            }

            var recognized = GraphicRecognition.RecognizeProject(loaded.Value, hints);
            var devices = recognized.SelectMany(r => r.Devices).ToList();
            var wires = recognized.SelectMany(r => r.Wires).ToList();
            if (devices.Count > 0 || wires.Count > 0)
            {
                var conv = GraphicRecognition.PlanConversion(
                    loaded.Value, devices, wires, DrawingAreaOriginFor(loaded.Value));
                if (conv.IsFailure)
                {
                    return Fail(error, conv.Error!);
                }

                var applied = stack.Execute(conv.Value.Transaction, ChangeSource.Import, who);
                if (applied.IsFailure)
                {
                    return Fail(error, applied.Error!);
                }

                var s = conv.Value.Summary;
                output.WriteLine(
                    $"Tanındı: {s.DevicesAdded} cihaz ({s.FunctionsAdded} fonksiyon, {devices.Sum(d => d.Pins.Count)} tel ucu), "
                    + $"{s.SymbolsAdded} sembol, {s.ConnectionsAdded} bağlantı ({s.GraphicsRemoved} serbest grafik dönüştürüldü).");

                if (!isDryRun && parse.GetValue(libraryPath) is { Length: > 0 } dbPath)
                {
                    var libCode = ExportRecognizedToLibrary(dbPath, devices, $"PDF: {Path.GetFileName(pdfPath)}", output, error);
                    if (libCode != ExitOk)
                    {
                        return libCode;
                    }
                }
            }
            else
            {
                output.WriteLine("Tanıma: dönüştürülebilir cihaz veya bağlantı bulunamadı.");
            }

            if (isDryRun)
            {
                output.WriteLine("Deneme çalıştırması: dosya yazılmadı.");
                return ExitOk;
            }

            var saved = Save(loaded.Value, projectPath, stack, output);
            return saved.IsFailure ? Fail(error, saved.Error!) : ExitOk;
        });
        return command;
    }

    private static Command ImportRecognizeCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Serbest grafik sayfaları içeren proje dosyası (.json)" };
        var pageNumber = new Option<string?>("--sayfa") { Description = "Yalnızca bu sayfa numarasını tara (varsayılan: tüm grafik sayfaları)" };
        var libraryPath = new Option<string?>("--kutuphane") { Description = "Tanınan sembol ve ürünleri bu kütüphaneye (.db) aktarır" };
        var keepGraphics = new Option<bool>("--grafikleri-koru") { Description = "Dönüştürülen serbest grafikleri silme" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Değişiklikleri göster, dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var command = new Command("recognize", "Projedeki serbest grafiklerden cihazları, tel uçlarını ve bağlantıları tanıyıp modele çevirir");
        command.Arguments.Add(project);
        command.Options.Add(pageNumber);
        command.Options.Add(libraryPath);
        command.Options.Add(keepGraphics);
        command.Options.Add(dryRun);
        command.Options.Add(actor);
        command.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            IReadOnlyList<PageRecognitionResult> results;
            if (parse.GetValue(pageNumber) is { Length: > 0 } targetNo)
            {
                var targetPage = loaded.Value.Pages.FirstOrDefault(p => p.Number == targetNo.Trim());
                if (targetPage is null)
                {
                    return Fail(error, new Error("page.yok", $"Sayfa '{targetNo}' projede bulunamadı."));
                }

                results = [GraphicRecognition.RecognizePage(loaded.Value, targetPage)];
            }
            else
            {
                results = GraphicRecognition.RecognizeProject(loaded.Value);
            }

            var devices = results.SelectMany(r => r.Devices).ToList();
            var wires = results.SelectMany(r => r.Wires).ToList();
            if (devices.Count == 0 && wires.Count == 0)
            {
                output.WriteLine("Dönüştürülebilir cihaz veya bağlantı bulunamadı.");
                return ExitOk;
            }

            var conv = GraphicRecognition.PlanConversion(
                loaded.Value,
                devices,
                wires,
                DrawingAreaOriginFor(loaded.Value),
                removeConsumedGraphics: !parse.GetValue(keepGraphics));
            if (conv.IsFailure)
            {
                return Fail(error, conv.Error!);
            }

            var s = conv.Value.Summary;
            output.WriteLine(
                $"Tanındı: {s.DevicesAdded} cihaz ({s.FunctionsAdded} fonksiyon, {devices.Sum(d => d.Pins.Count)} tel ucu), "
                + $"{s.SymbolsAdded} sembol, {s.ConnectionsAdded} bağlantı ({s.GraphicsRemoved} serbest grafik dönüştürüldü).");

            var isDryRun = parse.GetValue(dryRun);
            if (!isDryRun && parse.GetValue(libraryPath) is { Length: > 0 } dbPath)
            {
                var libCode = ExportRecognizedToLibrary(dbPath, devices, $"Proje: {Path.GetFileName(projectPath)}", output, error);
                if (libCode != ExitOk)
                {
                    return libCode;
                }
            }

            return Commit(loaded.Value, projectPath, conv.Value.Transaction, isDryRun, parse.GetValue(actor), output, error);
        });
        return command;
    }

    private static Func<Page, (double X, double Y)> DrawingAreaOriginFor(Project project) => page =>
    {
        var template = project.TemplateFor(page);
        if (template is null)
        {
            return (0, 0);
        }

        var da = SheetGeometry.For(template).DrawingArea;
        return (da.X, da.Y);
    };

    private static int ExportRecognizedToLibrary(
        string libraryPath,
        IReadOnlyList<RecognizedDevice> devices,
        string source,
        TextWriter output,
        TextWriter error)
    {
        var opened = ComponentLibrary.Open(libraryPath);
        if (opened.IsFailure)
        {
            return Fail(error, opened.Error!);
        }

        using var library = opened.Value;
        var exported = GraphicRecognition.ExportToLibrary(
            library, devices, "İçe aktarılan semboller", source, "Kullanıcı çizimi");
        if (exported.IsFailure)
        {
            return Fail(error, exported.Error!);
        }

        var summary = exported.Value;
        output.WriteLine(
            $"Kütüphane güncellendi: {summary.SymbolsAdded} sembol eklendi, {summary.SymbolsUpdated} güncellendi; "
            + $"{summary.ProductsAdded} ürün eklendi, {summary.ProductsUpdated} güncellendi.");
        return ExitOk;
    }
}
