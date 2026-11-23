using System.CommandLine;
using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Cli;

/// <summary><c>ake haberlesme</c>: haberleşme şemasını (bus topolojisi) dosyaya yazar veya projeye sayfa olarak ekler (ADR-0037).</summary>
internal static partial class CliApp
{
    private static Command CommunicationCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var target = new Option<string?>("--cikti") { Description = "Çıktı dosyası: .pdf, .png ya da .svg" };
        var addPage = new Option<bool>("--sayfa-ekle") { Description = "Haberleşme şemasını projeye kalıcı bir sayfa olarak ekler" };

        var command = new Command("haberlesme", "Haberleşme uçlarından ve bağlantı türlerinden haberleşme şemasını (bus topolojisi) çıkarır");
        command.Arguments.Add(project);
        command.Options.Add(target);
        command.Options.Add(addPage);
        command.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var path = parse.GetValue(target);
            var savePage = parse.GetValue(addPage);
            if (string.IsNullOrWhiteSpace(path) && !savePage)
            {
                return Fail(error, new Error("haberlesme.secenek_yok", "--cikti veya --sayfa-ekle seçeneklerinden en az biri gerekli."));
            }

            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var scene = CommunicationDiagram.Build(loaded.Value);
            if (scene.IsFailure)
            {
                return Fail(error, scene.Error!);
            }

            if (savePage)
            {
                var added = EnsureGeneratedPage(loaded.Value, projectPath, PageKind.CommunicationDiagram, "Haberleşme şeması", output, error);
                if (added != ExitOk)
                {
                    return added;
                }
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return ExitOk;
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            Result saved;
            try
            {
                switch (extension)
                {
                    case ".pdf":
                        saved = PdfExporter.Save([scene.Value], path, Path.GetDirectoryName(Path.GetFullPath(projectPath)), $"{loaded.Value.Name} haberleşme şeması");
                        break;
                    case ".png":
                        saved = PngExporter.Save(scene.Value, path, Path.GetDirectoryName(Path.GetFullPath(projectPath)));
                        break;
                    case ".svg":
                        File.WriteAllText(path, SvgExporter.Write(scene.Value));
                        saved = Result.Success();
                        break;
                    default:
                        return Fail(error, new Error("haberlesme.bicim", $"Çıktı .pdf, .png ya da .svg olmalı: '{path}'"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(error, new Error("render.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
            }

            if (saved.IsFailure)
            {
                return Fail(error, saved.Error!);
            }

            output.WriteLine($"Yazıldı: {path}");
            return ExitOk;
        });
        return command;
    }

    private static int EnsureGeneratedPage(Project project, string projectPath, PageKind kind, string title, TextWriter output, TextWriter error)
    {
        if (project.Pages.Any(p => p.Kind == kind))
        {
            output.WriteLine($"Sayfa zaten projede var: {title}");
            return ExitOk;
        }

        var commands = new List<ICommand>();
        var template = project.SheetTemplates.FirstOrDefault();
        if (template is null)
        {
            template = StandardSheetTemplates.A3Landscape;
            commands.Add(new PutSheetTemplate(template));
        }

        var used = project.Pages.Select(p => p.Number).ToHashSet(StringComparer.Ordinal);
        var next = project.Pages.Select(p => int.TryParse(p.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        while (used.Contains(next.ToString(CultureInfo.InvariantCulture)))
        {
            next++;
        }

        commands.Add(new AddPage(new Page(PageId.New(), next.ToString(CultureInfo.InvariantCulture), title, templateId: template.Id, kind: kind)));
        return Commit(project, projectPath, new Transaction($"{title} sayfası eklendi", commands), dryRun: false, actor: null, output, error);
    }
}
