using System.CommandLine;
using Ake.Core;
using Ake.Core.Model;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Cli;

/// <summary><c>ake tekhat</c>: güç zincirinden tek hat şemasını dosyaya yazar veya projeye ekler (ADR-0039).</summary>
internal static partial class CliApp
{
    private static Command SingleLineCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var target = new Option<string?>("--cikti") { Description = "Çıktı dosyası: .pdf, .png ya da .svg" };
        var addPage = new Option<bool>("--sayfa-ekle") { Description = "Tek hat şemasını projeye kalıcı bir sayfa olarak ekler" };

        var command = new Command("tekhat", "Besleme, koruma, kontaktör ve yük zincirinden tek hat şemasını çıkarır");
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
                return Fail(error, new Error("tekhat.secenek_yok", "--cikti veya --sayfa-ekle seçeneklerinden en az biri gerekli."));
            }

            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var scene = SingleLineDiagram.Build(loaded.Value);
            if (scene.IsFailure)
            {
                return Fail(error, scene.Error!);
            }

            if (savePage)
            {
                var added = EnsureGeneratedPage(loaded.Value, projectPath, PageKind.SingleLineDiagram, "Tek hat şeması", output, error);
                if (added != ExitOk)
                {
                    return added;
                }
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return ExitOk;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            Result saved;
            try
            {
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".pdf":
                        saved = PdfExporter.Save([scene.Value], path, directory, $"{loaded.Value.Name} tek hat şeması");
                        break;
                    case ".png":
                        saved = PngExporter.Save(scene.Value, path, directory);
                        break;
                    case ".svg":
                        File.WriteAllText(path, SvgExporter.Write(scene.Value));
                        saved = Result.Success();
                        break;
                    default:
                        return Fail(error, new Error("tekhat.bicim", $"Çıktı .pdf, .png ya da .svg olmalı: '{path}'"));
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
}
